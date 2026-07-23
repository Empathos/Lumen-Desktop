using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumen.App.Desktop;

/// <summary>
/// Full virtual-desktop capture for the perception channel (LD-023).
/// v1 uses GDI CopyFromScreen of the whole virtual desktop — this is reliable
/// for the *entire screen* (the composited-window limitation of GDI applies to
/// per-window PrintWindow, not full-screen grabs). DXGI Desktop Duplication is
/// the planned upgrade for lower latency and per-window/occluded capture
/// (RISK-004).
///
/// Output is downscaled and JPEG-encoded to keep the realtime image payload
/// small. The scale factor is returned so screen coordinates can be mapped
/// between image space and true desktop pixels.
/// </summary>
internal static class ScreenCapture
{
    public readonly record struct Capture(string JpegBase64, int TrueWidth, int TrueHeight, double Scale, int OriginX, int OriginY);

    public static Capture CaptureVirtualDesktop(int maxLongSide = 1536, long jpegQuality = 70)
    {
        var originX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var originY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        using var full = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(originX, originY, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        var scale = Math.Min(1.0, (double)maxLongSide / Math.Max(width, height));
        Bitmap outBmp = full;
        Bitmap? scaled = null;
        if (scale < 1.0)
        {
            var sw = (int)Math.Round(width * scale);
            var sh = (int)Math.Round(height * scale);
            scaled = new Bitmap(sw, sh, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, sw, sh);
            outBmp = scaled;
        }

        using var ms = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
        outBmp.Save(ms, encoder, p);
        scaled?.Dispose();

        return new Capture(Convert.ToBase64String(ms.ToArray()), width, height, scale, originX, originY);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
