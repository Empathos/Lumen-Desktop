namespace Lumen.Core.Realtime;

public sealed class RealtimeClientOptions
{
    public required string ApiKey { get; init; }
    public string Model { get; init; } = "gpt-realtime-2.1";
    public string Voice { get; init; } = "marin";
    public string ReasoningEffort { get; init; } = "low"; // speed-first default per ADR-0001
    /// <summary>semantic_vad eagerness: auto balances response speed vs false triggers
    /// (the echo gate in the audio layer handles speaker bleed).</summary>
    public string VadEagerness { get; init; } = "auto";
    public string? InputTranscriptionModel { get; init; } = "whisper-1";
    public string Instructions { get; init; } =
        "You are Lumen, a fast, concise voice copilot controlling a Windows 11 desktop. " +
        "You act through tools; narrate briefly what you are doing. " +
        "You can SEE the desktop: call look_at_screen to capture and view the whole screen — do this to orient yourself, find things, and verify results after acting. " +
        "To interact with an app: focus_window first, then read_screen_elements to see its controls, " +
        "then click_element by id (this presses controls invisibly without moving the user's mouse — prefer it). " +
        "Use mouse_click/mouse_drag with coordinates only when no element id works; coordinates appear in the element map after @. " +
        "Fill fields with set_element_value by id (background, no focus needed) and scroll lists with scroll_element; " +
        "fall back to type_text after clicking a field only if direct value-setting fails, and press_keys for shortcuts like ctrl+t or enter. " +
        "Re-read screen elements after the UI changes. " +
        "If a mouse action fails because the user is actively using the mouse, say so in a few words, wait a beat, and retry. " +
        "If the user's intent is ambiguous, ask a short clarifying question instead of guessing. " +
        "Keep spoken replies short.";
    public Uri Endpoint { get; init; } = new("wss://api.openai.com/v1/realtime");
}
