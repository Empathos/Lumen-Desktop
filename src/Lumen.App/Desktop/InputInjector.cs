using System.Runtime.InteropServices;

namespace Lumen.App.Desktop;

/// <summary>
/// Real-cursor mouse and keyboard injection via SendInput (LD-011).
/// Used when an element has no UIA pattern, for free-form coordinates,
/// drags, typing, and shortcuts. Mouse actions borrow the user's cursor
/// and hand it back (LD-028 cursor loan).
/// </summary>
internal static class InputInjector
{
    // ---------- mouse ----------

    /// <summary>One injected gesture at a time — parallel tool calls must not
    /// interleave their SendInput streams.</summary>
    private static readonly object Gate = new();

    /// <summary>Hover is the point of a move, so the real cursor stays at the
    /// target — no restore. Still refuses to steal an active drag.</summary>
    public static bool MouseMove(int x, int y)
    {
        lock (Gate)
        {
            GetCursorPos(out var preGlide);
            AgentCursor.GlideToBlocking(x, y);
            if (!TryBorrowCursor(preGlide, out _)) return false;
            SendMouse(x, y, MOUSEEVENTF_MOVE);
            return true;
        }
    }

    public static bool MouseClick(int x, int y, string button, int clicks)
    {
        lock (Gate)
        {
            GetCursorPos(out var preGlide);
            AgentCursor.GlideToBlocking(x, y);
            if (!TryBorrowCursor(preGlide, out var home)) return false;
            AgentCursor.Pulse();
            var (down, up) = button.ToLowerInvariant() switch
            {
                "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
                "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
                _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
            };
            SendMouse(x, y, MOUSEEVENTF_MOVE);
            Thread.Sleep(20);
            for (var i = 0; i < Math.Max(1, clicks); i++)
            {
                // MOVE on down/up pins each event to the target even if the
                // user's hand wiggles mid-sequence.
                SendMouse(x, y, down | MOUSEEVENTF_MOVE);
                SendMouse(x, y, up | MOUSEEVENTF_MOVE);
                if (i < clicks - 1) Thread.Sleep(60); // double-click cadence
            }
            ReturnCursor(home, x, y);
            return true;
        }
    }

    public static bool MouseDrag(int fromX, int fromY, int toX, int toY)
    {
        lock (Gate)
        {
            GetCursorPos(out var preGlide);
            AgentCursor.GlideToBlocking(fromX, fromY);
            if (!TryBorrowCursor(preGlide, out var home)) return false;
            DragCore(fromX, fromY, toX, toY);
            ReturnCursor(home, toX, toY);
            return true;
        }
    }

    private static void DragCore(int fromX, int fromY, int toX, int toY)
    {
        SendMouse(fromX, fromY, MOUSEEVENTF_MOVE);
        Thread.Sleep(30);
        SendMouse(fromX, fromY, MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(60);
        const int steps = 24;
        for (var i = 1; i <= steps; i++)
        {
            var x = fromX + (toX - fromX) * i / steps;
            var y = fromY + (toY - fromY) * i / steps;
            SendMouse(x, y, MOUSEEVENTF_MOVE);
            if (i % 3 == 0) AgentCursor.JumpTo(x, y);
            Thread.Sleep(8);
        }
        Thread.Sleep(60);
        SendMouse(toX, toY, MOUSEEVENTF_LEFTUP);
        AgentCursor.JumpTo(toX, toY);
    }

    // ---------- cursor loan (LD-028) ----------
    // SendInput drives the one real cursor, so each action borrows it: the
    // agent-cursor glide doubles as a stillness window, the user's position
    // is saved, the action runs pinned to its target, and the cursor goes
    // back. A post-action position away from the target means the user
    // grabbed the mouse mid-action — their movement wins, no restore.
    //
    // A held mouse button is an active manipulation (drag, selection, window
    // move) — position stillness alone can't detect a slow drag, and stealing
    // the cursor mid-drag wrecks it. So: no borrow while a physical button is
    // down; if the user stays busy past the patience window the action is
    // refused (false) and the model gets a busy error to relay/retry.

    private static bool TryBorrowCursor(POINT preGlide, out POINT home, int patienceMs = 2500)
    {
        var deadline = Environment.TickCount64 + patienceMs;
        GetCursorPos(out var last);
        var still = last.X == preGlide.X && last.Y == preGlide.Y && !AnyMouseButtonDown();
        while (!still)
        {
            if (Environment.TickCount64 > deadline) { home = last; return false; }
            Thread.Sleep(40);
            GetCursorPos(out var now);
            still = now.X == last.X && now.Y == last.Y && !AnyMouseButtonDown();
            last = now;
        }
        home = last;
        return true;
    }

    private static bool AnyMouseButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

    private static void ReturnCursor(POINT home, int landX, int landY)
    {
        GetCursorPos(out var now);
        // ±3px covers the 0..65535 absolute-coordinate rounding.
        if (Math.Abs(now.X - landX) <= 3 && Math.Abs(now.Y - landY) <= 3)
            SetCursorPos(home.X, home.Y);
    }

    private static void SendMouse(int x, int y, uint flags)
    {
        // Normalize to the virtual desktop (multi-monitor safe).
        var vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vWidth = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var vHeight = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = (x - vLeft) * 65535 / vWidth,
                    dy = (y - vTop) * 65535 / vHeight,
                    dwFlags = flags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                }
            }
        };
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    // ---------- keyboard ----------

    public static void TypeText(string text)
    {
        lock (Gate)
        {
            foreach (var ch in text)
            {
                if (ch == '\n') { PressVk(VK_RETURN); continue; }
                var inputs = new[]
                {
                    KeyboardInput(0, ch, KEYEVENTF_UNICODE),
                    KeyboardInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)
                };
                _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                Thread.Sleep(4); // gentle pacing keeps slow apps from dropping chars
            }
        }
    }

    /// <summary>Press a combo like "ctrl+shift+t", "alt+f4", "enter", "win+r".</summary>
    public static void PressCombo(string combo)
    {
        lock (Gate)
        {
            var parts = combo.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var vks = parts.Select(ToVk).Where(v => v != 0).ToArray();
            foreach (var vk in vks) KeyEvent(vk, false);
            Thread.Sleep(20);
            foreach (var vk in vks.Reverse()) KeyEvent(vk, true);
        }
    }

    private static void PressVk(ushort vk)
    {
        KeyEvent(vk, false);
        KeyEvent(vk, true);
    }

    private static void KeyEvent(ushort vk, bool up)
    {
        var input = KeyboardInput(vk, '\0', up ? KEYEVENTF_KEYUP : 0);
        _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyboardInput(ushort vk, char scanChar, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = scanChar,
                dwFlags = flags
            }
        }
    };

    private static ushort ToVk(string key) => key switch
    {
        "ctrl" or "control" => 0x11,
        "alt" => 0x12,
        "shift" => 0x10,
        "win" or "windows" or "cmd" => 0x5B,
        "enter" or "return" => VK_RETURN,
        "tab" => 0x09,
        "esc" or "escape" => 0x1B,
        "space" => 0x20,
        "backspace" => 0x08,
        "delete" or "del" => 0x2E,
        "home" => 0x24,
        "end" => 0x23,
        "pageup" => 0x21,
        "pagedown" => 0x22,
        "up" => 0x26,
        "down" => 0x28,
        "left" => 0x25,
        "right" => 0x27,
        _ when key.Length == 1 && key[0] is >= 'a' and <= 'z' => (ushort)(key[0] - 'a' + 0x41),
        _ when key.Length == 1 && key[0] is >= '0' and <= '9' => (ushort)(key[0] - '0' + 0x30),
        _ when key.Length is 2 or 3 && key[0] == 'f' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12
            => (ushort)(0x70 + f - 1),
        _ => 0
    };

    // ---------- Win32 ----------

    private const ushort VK_RETURN = 0x0D;
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_MBUTTON = 0x04;
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
