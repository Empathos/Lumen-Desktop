using System.Runtime.InteropServices;

namespace Lumen.App.Desktop;

/// <summary>
/// Lumen's second pointer (LD-030): synthetic touch injection. Windows treats
/// touch as a separate pointer from the mouse, so her taps land while the user
/// keeps full control of their cursor — no loan, no waiting. Pointer-aware
/// apps see a true second input; legacy apps get Windows' touch→mouse
/// emulation, which can teleport the real cursor to the tap (the caller
/// restores it). Unavailable environments (some remote sessions) make
/// Init fail and every Tap return false, falling back to the cursor loan.
/// </summary>
internal static class TouchInjector
{
    private static readonly bool Available = Init();

    private static bool Init()
    {
        try { return InitializeTouchInjection(1, TOUCH_FEEDBACK_DEFAULT); }
        catch { return false; }
    }

    /// <summary>Tap (or multi-tap) at screen pixels. False when touch injection is unavailable or rejected.</summary>
    public static bool Tap(int x, int y, int taps)
    {
        if (!Available) return false;
        for (var i = 0; i < Math.Max(1, taps); i++)
        {
            if (!Send(x, y, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT)) return false;
            Thread.Sleep(35);
            if (!Send(x, y, POINTER_FLAG_UP)) return false;
            if (i < taps - 1) Thread.Sleep(90); // double-tap cadence
        }
        return true;
    }

    private static bool Send(int x, int y, uint flags)
    {
        var contact = new POINTER_TOUCH_INFO
        {
            pointerInfo = new POINTER_INFO
            {
                pointerType = PT_TOUCH,
                pointerId = 0,
                pointerFlags = flags,
                ptPixelLocation = new POINT { X = x, Y = y }
            },
            touchFlags = 0,
            touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_ORIENTATION | TOUCH_MASK_PRESSURE,
            rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 },
            orientation = 90,
            pressure = 32000
        };
        return InjectTouchInput(1, new[] { contact });
    }

    // ---------- Win32 ----------

    private const uint PT_TOUCH = 2;
    private const uint TOUCH_FEEDBACK_DEFAULT = 1;
    private const uint POINTER_FLAG_INRANGE = 0x2;
    private const uint POINTER_FLAG_INCONTACT = 0x4;
    private const uint POINTER_FLAG_DOWN = 0x10000;
    private const uint POINTER_FLAG_UP = 0x40000;
    private const uint TOUCH_MASK_CONTACTAREA = 0x1;
    private const uint TOUCH_MASK_ORIENTATION = 0x2;
    private const uint TOUCH_MASK_PRESSURE = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType;
        public uint pointerId;
        public uint frameId;
        public uint pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public POINT ptPixelLocation;
        public POINT ptHimetricLocation;
        public POINT ptPixelLocationRaw;
        public POINT ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public uint touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InitializeTouchInjection(uint maxCount, uint feedbackMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InjectTouchInput(uint count, POINTER_TOUCH_INFO[] contacts);
}
