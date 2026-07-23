using Lumen.Core.Realtime;

namespace Lumen.App.Desktop;

/// <summary>
/// Pushes desktop frames into a streaming-capable session (Gemini Live) at a
/// fixed low rate (≤1 fps per Gemini spec) to give the model ambient sight
/// (LD-024). Skips frames while a send is still in flight to avoid backing up.
/// </summary>
internal sealed class VideoStreamer : IDisposable
{
    private readonly IVoiceSession _session;
    private readonly int _intervalMs;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public VideoStreamer(IVoiceSession session, int fps = 1)
    {
        _session = session;
        _intervalMs = Math.Max(200, 1000 / Math.Max(1, fps));
    }

    public void Start()
    {
        if (!_session.SupportsVideoStream) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var cap = ScreenCapture.CaptureVirtualDesktop(maxLongSide: 1280, jpegQuality: 60);
                await _session.SendImageAsync(cap.JpegBase64, null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* transient capture/send error; keep streaming */ }

            try { await Task.Delay(_intervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { }
        _cts?.Dispose();
    }
}
