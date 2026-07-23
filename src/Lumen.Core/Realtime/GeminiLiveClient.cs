using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Lumen.Core.Tools;

namespace Lumen.Core.Realtime;

public sealed class GeminiLiveOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "gemini-3.1-flash-live-preview";
    public string Voice { get; init; } = "Kore";
    public string ThinkingLevel { get; init; } = "minimal"; // speed-first
    public string Instructions { get; init; } =
        "You are Lumen, a fast, concise voice copilot with continuous sight of a Windows 11 desktop. " +
        "You can see the screen streaming live, so describe and act on what is actually visible. " +
        "Act through tools; narrate briefly. Prefer click_element, set_element_value and scroll_element (invisible, no cursor move) over coordinates. " +
        "If a mouse action fails because the user is actively using the mouse, say so in a few words, wait a beat, and retry. " +
        "Ask a short clarifying question when intent is ambiguous. Keep spoken replies short.";
    public Uri Endpoint { get; init; } = new(
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent");
}

/// <summary>
/// Gemini Live provider (LD-024): a persistent WebSocket carrying audio AND a
/// continuous screen video stream (≤1 fps JPEG), giving the model ambient sight
/// of the desktop instead of on-demand screenshots. Implements the same
/// IVoiceSession contract as the OpenAI client for A/B testing.
/// Input PCM 16 kHz, output PCM 24 kHz per Gemini Live spec.
/// </summary>
public sealed class GeminiLiveClient : IVoiceSession
{
    private readonly GeminiLiveOptions _options;
    private readonly ToolRegistry _tools;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public int InputSampleRate => 16000;
    public bool SupportsVideoStream => true;

    public event Action<string, bool>? UserTranscript;
    public event Action<string>? AssistantTranscript;
    public event Action<byte[]>? AudioOut;
    public event Action? SpeechStarted;
    public event Action<string, string>? ToolInvoked;
    public event Action<string>? Status;

    public GeminiLiveClient(GeminiLiveOptions options, ToolRegistry tools)
    {
        _options = options;
        _tools = tools;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        var uri = new Uri($"{_options.Endpoint}?key={Uri.EscapeDataString(_options.ApiKey)}");
        await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);
        Status?.Invoke("connected (gemini)");

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        await SendSetupAsync(_cts.Token).ConfigureAwait(false);
    }

    private Task SendSetupAsync(CancellationToken ct)
    {
        var functionDeclarations = _tools.All.Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
        }).ToArray();

        return SendJsonAsync(new
        {
            setup = new
            {
                model = $"models/{_options.Model}",
                generationConfig = new
                {
                    responseModalities = new[] { "AUDIO" },
                    speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = _options.Voice } } },
                    thinkingConfig = new { thinkingLevel = _options.ThinkingLevel }
                },
                systemInstruction = new { parts = new[] { new { text = _options.Instructions } } },
                tools = new[] { new { functionDeclarations } },
                realtimeInputConfig = new { automaticActivityDetection = new { disabled = false } },
                inputAudioTranscription = new { },
                outputAudioTranscription = new { }
            }
        }, ct);
    }

    public Task SendAudioAsync(byte[] pcm16, CancellationToken ct = default) =>
        SendJsonAsync(new
        {
            realtimeInput = new
            {
                mediaChunks = new[]
                {
                    new { mimeType = "audio/pcm;rate=16000", data = Convert.ToBase64String(pcm16) }
                }
            }
        }, ct);

    public Task SendImageAsync(string jpegBase64, string? caption = null, CancellationToken ct = default) =>
        SendJsonAsync(new
        {
            realtimeInput = new
            {
                mediaChunks = new[] { new { mimeType = "image/jpeg", data = jpegBase64 } }
            }
        }, ct);

    public Task CancelResponseAsync(CancellationToken ct = default) => Task.CompletedTask; // Gemini VAD auto-interrupts

    public Task InjectContextAsync(string text, CancellationToken ct = default) =>
        SendJsonAsync(new
        {
            clientContent = new
            {
                turns = new[] { new { role = "user", parts = new[] { new { text } } } },
                turnComplete = false
            }
        }, ct);

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try { await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
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
                        var why = result.CloseStatusDescription;
                        Status?.Invoke(string.IsNullOrWhiteSpace(why) ? $"gemini closed ({(int?)result.CloseStatus})" : $"gemini closed: {why}");
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleEvent(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status?.Invoke($"gemini recv error: {ex.Message}"); }
    }

    private void HandleEvent(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("setupComplete", out _)) { Status?.Invoke("session ready"); return; }

        if (root.TryGetProperty("serverContent", out var sc))
        {
            if (sc.TryGetProperty("interrupted", out var intr) && intr.ValueKind == JsonValueKind.True)
                SpeechStarted?.Invoke();

            if (sc.TryGetProperty("outputTranscription", out var ot) &&
                ot.TryGetProperty("text", out var otText) && otText.GetString() is { } at)
                AssistantTranscript?.Invoke(at);

            if (sc.TryGetProperty("inputTranscription", out var it) &&
                it.TryGetProperty("text", out var itText) && itText.GetString() is { } ut)
                UserTranscript?.Invoke(ut, false);

            if (sc.TryGetProperty("modelTurn", out var mt) &&
                mt.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("inlineData", out var inline) &&
                        inline.TryGetProperty("data", out var d) && d.GetString() is { } b64)
                        AudioOut?.Invoke(Convert.FromBase64String(b64));
                    if (part.TryGetProperty("text", out var pt) && pt.GetString() is { } ptext)
                        AssistantTranscript?.Invoke(ptext);
                }
            }
            return;
        }

        if (root.TryGetProperty("toolCall", out var tc) &&
            tc.TryGetProperty("functionCalls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var id = call.TryGetProperty("id", out var cid) ? cid.GetString() : null;
                var name = call.TryGetProperty("name", out var n) ? n.GetString() : null;
                var args = call.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                if (name is null) continue;
                ToolInvoked?.Invoke(name, args);
                _ = Task.Run(async () =>
                {
                    var res = await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false);
                    JsonElement resObj;
                    try { resObj = JsonSerializer.Deserialize<JsonElement>(res); }
                    catch { resObj = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { result = res })); }
                    await SendJsonAsync(new
                    {
                        toolResponse = new
                        {
                            functionResponses = new[] { new { id, name, response = resObj } }
                        }
                    }, ct).ConfigureAwait(false);
                }, ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_ws is { State: WebSocketState.Open })
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
        if (_receiveLoop is not null) { try { await _receiveLoop.ConfigureAwait(false); } catch { } }
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
