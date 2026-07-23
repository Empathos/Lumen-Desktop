namespace Lumen.Core.Realtime;

/// <summary>
/// Provider-agnostic voice+perception session (LD-025). Both the OpenAI
/// Realtime client and the Gemini Live client implement this, so the app,
/// audio loop, and action engine are written once and the brain is swappable
/// for A/B testing (RISK-002 mitigation, now spanning perception too).
/// </summary>
public interface IVoiceSession : IAsyncDisposable
{
    event Action<string, bool>? UserTranscript;
    event Action<string>? AssistantTranscript;
    event Action<byte[]>? AudioOut;
    event Action? SpeechStarted;
    event Action<string, string>? ToolInvoked;
    event Action<string>? Status;

    /// <summary>PCM16 sample rate this provider expects for microphone input.</summary>
    int InputSampleRate { get; }

    /// <summary>True if the provider supports a continuous video/screen stream (ambient sight).</summary>
    bool SupportsVideoStream { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task SendAudioAsync(byte[] pcm16, CancellationToken ct = default);

    /// <summary>Send one screen frame. For streaming providers, call at ≤1 fps for ambient vision.</summary>
    Task SendImageAsync(string jpegBase64, string? caption = null, CancellationToken ct = default);

    Task CancelResponseAsync(CancellationToken ct = default);
    Task InjectContextAsync(string text, CancellationToken ct = default);
}
