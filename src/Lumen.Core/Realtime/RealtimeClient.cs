using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lumen.Core.Tools;

namespace Lumen.Core.Realtime;

/// <summary>
/// Lean WebSocket client for the OpenAI Realtime API (LD-001).
/// One receive loop, event callbacks, tool dispatch. No UI, no OS calls.
/// </summary>
public sealed class RealtimeClient : IVoiceSession
{
    public int InputSampleRate => 24000;
    public bool SupportsVideoStream => false; // OpenAI realtime: on-demand screenshots, not a stream

    private readonly RealtimeClientOptions _options;
    private readonly ToolRegistry _tools;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly HashSet<string> _handledCalls = new();
    private volatile bool _responseActive;

    /// <summary>Delta of the user's live transcription. Final=true when the utterance completes.</summary>
    public event Action<string, bool>? UserTranscript;
    /// <summary>Delta of the assistant's spoken-reply transcript.</summary>
    public event Action<string>? AssistantTranscript;
    /// <summary>Raw PCM16 (24 kHz mono) audio chunk to play.</summary>
    public event Action<byte[]>? AudioOut;
    /// <summary>User started speaking — host should immediately stop/clear playback (barge-in, LD-003).</summary>
    public event Action? SpeechStarted;
    /// <summary>A tool was invoked (name, args) — observational, for the transcript/action log.</summary>
    public event Action<string, string>? ToolInvoked;
    /// <summary>Connection status / recoverable errors, human-readable.</summary>
    public event Action<string>? Status;

    public RealtimeClient(RealtimeClientOptions options, ToolRegistry tools)
    {
        _options = options;
        _tools = tools;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {_options.ApiKey}");

        var uri = new Uri($"{_options.Endpoint}?model={Uri.EscapeDataString(_options.Model)}");
        await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);
        Status?.Invoke("connected");

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        await SendSessionUpdateAsync(_cts.Token).ConfigureAwait(false);
        await SendTranscriptionUpdateAsync(_cts.Token).ConfigureAwait(false);
    }

    /// <summary>Append raw PCM16 24 kHz mono mic audio.</summary>
    public Task SendAudioAsync(byte[] pcm16, CancellationToken ct = default) =>
        SendJsonAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16) }, ct);

    /// <summary>Cancel the in-flight response (used on barge-in).</summary>
    public Task CancelResponseAsync(CancellationToken ct = default) =>
        SendJsonAsync(new { type = "response.cancel" }, ct);

    /// <summary>Inject a screenshot into the conversation so the model can see the desktop (LD-023).</summary>
    public Task SendImageAsync(string jpegBase64, string? caption = null, CancellationToken ct = default)
    {
        var content = new List<object>
        {
            new { type = "input_image", image_url = $"data:image/jpeg;base64,{jpegBase64}" }
        };
        if (!string.IsNullOrEmpty(caption))
            content.Insert(0, new { type = "input_text", text = caption });

        return SendJsonAsync(new
        {
            type = "conversation.item.create",
            item = new { type = "message", role = "user", content }
        }, ct);
    }

    /// <summary>Inject a context item out-of-band (Alice injector channel, LD-020).</summary>
    public Task InjectContextAsync(string text, CancellationToken ct = default) =>
        SendJsonAsync(new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "system",
                content = new[] { new { type = "input_text", text } }
            }
        }, ct);

    private Task SendSessionUpdateAsync(CancellationToken ct)
    {
        var tools = _tools.All.Select(t => new
        {
            type = "function",
            name = t.Name,
            description = t.Description,
            parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
        }).ToArray();

        return SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                model = _options.Model,
                output_modalities = new[] { "audio" },
                instructions = _options.Instructions,
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        turn_detection = new { type = "semantic_vad", eagerness = _options.VadEagerness }
                    },
                    output = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        voice = _options.Voice
                    }
                },
                tools
            }
        }, ct);
    }

    /// <summary>
    /// Input transcription is sent as a second, separate update so that a
    /// rejected transcription model cannot invalidate the core session config.
    /// </summary>
    private Task SendTranscriptionUpdateAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.InputTranscriptionModel)) return Task.CompletedTask;
        return SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                audio = new
                {
                    input = new
                    {
                        transcription = new { model = _options.InputTranscriptionModel }
                    }
                }
            }
        }, ct);
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[1 << 16];
        var message = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws is { State: WebSocketState.Open })
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // The close frame carries the why (e.g. insufficient_quota) — never drop it.
                        var why = result.CloseStatusDescription;
                        Status?.Invoke(string.IsNullOrWhiteSpace(why)
                            ? $"server closed connection ({(int?)result.CloseStatus})"
                            : $"server closed: {why}");
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleEvent(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), ct);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            Status?.Invoke($"receive loop error: {ex.Message}");
        }
    }

    private void HandleEvent(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            // ---- audio out (GA name, then legacy fallback) ----
            case "response.output_audio.delta":
            case "response.audio.delta":
                if (root.TryGetProperty("delta", out var audio) && audio.GetString() is { } b64)
                    AudioOut?.Invoke(Convert.FromBase64String(b64));
                break;

            // ---- assistant transcript ----
            case "response.output_audio_transcript.delta":
            case "response.audio_transcript.delta":
                if (root.TryGetProperty("delta", out var atd) && atd.GetString() is { } atext)
                    AssistantTranscript?.Invoke(atext);
                break;

            // ---- user transcript ----
            case "conversation.item.input_audio_transcription.delta":
                if (root.TryGetProperty("delta", out var utd) && utd.GetString() is { } utext)
                    UserTranscript?.Invoke(utext, false);
                break;
            case "conversation.item.input_audio_transcription.completed":
                if (root.TryGetProperty("transcript", out var utc) && utc.GetString() is { } ufinal)
                    UserTranscript?.Invoke(ufinal, true);
                break;

            // ---- barge-in ----
            case "input_audio_buffer.speech_started":
                SpeechStarted?.Invoke();
                if (_responseActive) _ = CancelResponseAsync(ct); // cancel only a live response
                break;

            case "response.created":
                _responseActive = true;
                break;

            // ---- tool calls: prefer per-item completion (supports parallel calls) ----
            case "response.output_item.done":
                if (root.TryGetProperty("item", out var item))
                    TryDispatchFunctionCall(item, ct);
                break;
            case "response.done":
                _responseActive = false;
                if (root.TryGetProperty("response", out var resp) &&
                    resp.TryGetProperty("output", out var output) &&
                    output.ValueKind == JsonValueKind.Array)
                {
                    foreach (var outItem in output.EnumerateArray())
                        TryDispatchFunctionCall(outItem, ct);
                }
                break;

            case "error":
                var msg = root.TryGetProperty("error", out var err) &&
                          err.TryGetProperty("message", out var em)
                          ? em.GetString() : json;
                Status?.Invoke($"api error: {msg}");
                break;

            case "session.created":
                Status?.Invoke("session ready");
                break;
        }
    }

    private void TryDispatchFunctionCall(JsonElement item, CancellationToken ct)
    {
        if (!item.TryGetProperty("type", out var it) || it.GetString() != "function_call") return;
        var callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() : null;
        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
        var args = item.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";
        if (callId is null || name is null) return;

        lock (_handledCalls)
        {
            if (!_handledCalls.Add(callId)) return; // already dispatched via the other event
        }

        ToolInvoked?.Invoke(name, args);

        // Run the tool off the receive loop; parallel calls run concurrently (LD-004).
        _ = Task.Run(async () =>
        {
            var result = await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false);
            await SendJsonAsync(new
            {
                type = "conversation.item.create",
                item = new { type = "function_call_output", call_id = callId, output = result }
            }, ct).ConfigureAwait(false);
            await SendJsonAsync(new { type = "response.create" }, ct).ConfigureAwait(false);
        }, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { /* best effort */ }
        }
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); } catch { /* observed */ }
        }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
