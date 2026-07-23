using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lumen.Core.Adapters;

namespace Lumen.App.Desktop;

/// <summary>
/// Windows implementation of the OS adapter ("hands"). Walking-skeleton scope:
/// launch, focus, list windows. UIA element maps + SendInput arrive in iteration 2.
/// </summary>
public sealed class WindowsDesktopActions : IDesktopActions
{
    private readonly UiaScreenReader _uia = new();

    public Task<string> GetScreenElementsAsync(CancellationToken ct) =>
        Task.Run(_uia.BuildMap, ct);

    public Task<string> ClickElementAsync(string elementId, CancellationToken ct) =>
        Task.Run(() => _uia.ClickById(elementId), ct);

    public Task<string> SetElementValueAsync(string elementId, string value, CancellationToken ct) =>
        Task.Run(() => _uia.SetValueById(elementId, value), ct);

    public Task<string> ScrollElementAsync(string elementId, string direction, int amount, CancellationToken ct) =>
        Task.Run(() => _uia.ScrollById(elementId, direction, amount), ct);

    private const string UserBusyError =
        "user is actively using the mouse; not acting to avoid disrupting them — tell them briefly and retry in a moment";

    public Task<string> MouseClickAsync(int x, int y, string button, int clicks, CancellationToken ct) =>
        Task.Run(() => InputInjector.MouseClick(x, y, button, clicks)
            ? JsonSerializer.Serialize(new { ok = true, clicked = new { x, y, button, clicks } })
            : JsonSerializer.Serialize(new { ok = false, error = UserBusyError }), ct);

    public Task<string> MouseMoveAsync(int x, int y, CancellationToken ct) =>
        Task.Run(() => InputInjector.MouseMove(x, y)
            ? JsonSerializer.Serialize(new { ok = true, moved = new { x, y } })
            : JsonSerializer.Serialize(new { ok = false, error = UserBusyError }), ct);

    public Task<string> MouseDragAsync(int fromX, int fromY, int toX, int toY, CancellationToken ct) =>
        Task.Run(() => InputInjector.MouseDrag(fromX, fromY, toX, toY)
            ? JsonSerializer.Serialize(new { ok = true, dragged = new { fromX, fromY, toX, toY } })
            : JsonSerializer.Serialize(new { ok = false, error = UserBusyError }), ct);

    public Task<string> TypeTextAsync(string text, bool pressEnter, CancellationToken ct) =>
        Task.Run(() =>
        {
            InputInjector.TypeText(pressEnter ? text + "\n" : text);
            return JsonSerializer.Serialize(new { ok = true, typed = text.Length, pressedEnter = pressEnter });
        }, ct);

    public Task<string> PressKeysAsync(string combo, CancellationToken ct) =>
        Task.Run(() =>
        {
            InputInjector.PressCombo(combo);
            return JsonSerializer.Serialize(new { ok = true, pressed = combo });
        }, ct);

    public Task<string> LaunchAppAsync(string name, CancellationToken ct)
    {
        try
        {
            // ShellExecute resolves PATH entries, App Paths registrations
            // ("chrome", "notepad") and full paths alike.
            Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
            return Task.FromResult(JsonSerializer.Serialize(new { ok = true, launched = name }));
        }
        catch (Exception)
        {
            try
            {
                // Fallback: let the shell's `start` builtin resolve Store apps / aliases.
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{name}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, launched = name, via = "shell" }));
            }
            catch (Exception ex2)
            {
                return Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = ex2.Message }));
            }
        }
    }

    public Task<string> FocusWindowAsync(string titleContains, CancellationToken ct)
    {
        var target = FindWindowByTitle(titleContains);
        if (target == IntPtr.Zero)
            return Task.FromResult(JsonSerializer.Serialize(new { ok = false, error = $"no window title contains '{titleContains}'" }));

        ShowWindow(target, SW_RESTORE);
        SetForegroundWindow(target);
        return Task.FromResult(JsonSerializer.Serialize(new { ok = true, focused = GetTitle(target) }));
    }

    public Task<IReadOnlyList<string>> ListWindowTitlesAsync(CancellationToken ct)
    {
        var titles = new List<string>();
        EnumWindows((hWnd, _) =>
        {
            if (IsWindowVisible(hWnd))
            {
                var title = GetTitle(hWnd);
                if (!string.IsNullOrWhiteSpace(title)) titles.Add(title);
            }
            return true;
        }, IntPtr.Zero);
        return Task.FromResult<IReadOnlyList<string>>(titles);
    }

    private static IntPtr FindWindowByTitle(string contains)
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = GetTitle(hWnd);
            if (title.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ---- Win32 ----
    private const int SW_RESTORE = 9;
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
