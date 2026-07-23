namespace Lumen.Core.Adapters;

/// <summary>
/// The OS adapter contract ("hands"). The core never touches an OS API;
/// each platform implements this. Iteration 2 adds the UIA-first action set
/// (LD-009/LD-011): element maps + background invokes, with real-cursor
/// mouse/keyboard as the fallback.
/// </summary>
public interface IDesktopActions
{
    // ---- apps & windows ----
    Task<string> LaunchAppAsync(string name, CancellationToken ct);
    Task<string> FocusWindowAsync(string titleContains, CancellationToken ct);
    Task<IReadOnlyList<string>> ListWindowTitlesAsync(CancellationToken ct);

    // ---- observation (UIA-first, LD-009) ----
    /// <summary>Compact indexed map of interactive elements in the focused window.</summary>
    Task<string> GetScreenElementsAsync(CancellationToken ct);

    // ---- element actions (background where possible) ----
    /// <summary>Activate an element from the last map. Uses UIA patterns (no cursor move) when available.</summary>
    Task<string> ClickElementAsync(string elementId, CancellationToken ct);

    /// <summary>Set a field's value directly — no cursor, no keyboard focus (LD-029).</summary>
    Task<string> SetElementValueAsync(string elementId, string value, CancellationToken ct);

    /// <summary>Scroll the container at/above an element — no cursor (LD-029).</summary>
    Task<string> ScrollElementAsync(string elementId, string direction, int amount, CancellationToken ct);

    // ---- raw input (real cursor / keyboard, LD-011) ----
    Task<string> MouseClickAsync(int x, int y, string button, int clicks, CancellationToken ct);
    Task<string> MouseMoveAsync(int x, int y, CancellationToken ct);
    Task<string> MouseDragAsync(int fromX, int fromY, int toX, int toY, CancellationToken ct);
    Task<string> TypeTextAsync(string text, bool pressEnter, CancellationToken ct);
    Task<string> PressKeysAsync(string combo, CancellationToken ct);
}
