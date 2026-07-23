using System.Runtime.InteropServices;

namespace Lumen.App;

/// <summary>
/// Liquid-glass chrome (LD-006/LD-007): Windows acrylic blur-behind with a
/// live-adjustable tint, plus Windows 11 native rounded corners so the
/// acrylic backdrop is clipped round too. All failures are cosmetic-only.
/// </summary>
internal static class Acrylic
{
    /// <summary>Enable acrylic + rounded corners. Call once on SourceInitialized.</summary>
    public static void TryEnable(IntPtr hwnd, byte tintAlpha = 0x99)
    {
        TryRoundCorners(hwnd);
        SetTint(hwnd, tintAlpha);
    }

    /// <summary>
    /// Re-apply the acrylic accent with a new tint alpha (0 = clear glass,
    /// 255 = opaque). Bound to the transparency slider.
    /// </summary>
    public static void SetTint(IntPtr hwnd, byte alpha)
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                // AABBGGRR — dark tint, caller-controlled alpha.
                GradientColor = unchecked((int)(((uint)alpha << 24) | 0x00181410))
            };

            var accentPtr = Marshal.AllocHGlobal(Marshal.SizeOf(accent));
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    SizeOfData = Marshal.SizeOf(accent),
                    Data = accentPtr
                };
                _ = SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch
        {
            // cosmetic only
        }
    }

    private static void TryRoundCorners(IntPtr hwnd)
    {
        try
        {
            var pref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // pre-Win11 builds: no rounding, no harm
        }
    }

    // ---- interop ----
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
