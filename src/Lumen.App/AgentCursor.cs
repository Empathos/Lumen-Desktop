using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Lumen.App;

/// <summary>
/// Lumen's own visible cursor (LD-026) — the on-screen half of the MouseMux
/// idea. A tiny always-on-top, click-through, non-activating overlay renders
/// a distinct teal pointer that glides to wherever Lumen is acting. Combined
/// with UIA background invokes and synthetic touch taps (LD-030), her pointer
/// moves and "clicks" without the user's real cursor being touched; drags and
/// right-clicks still borrow the real cursor for an instant and hand it back
/// (LD-028 loan). Session-level independence remains IDEA-006 on the register.
/// </summary>
public static class AgentCursor
{
    private static CursorWindow? _win;

    public static void Show() => OnUi(() =>
    {
        _win ??= new CursorWindow();
        _win.Show();
    });

    public static void Hide() => OnUi(() => _win?.Hide());

    /// <summary>Animate to a physical-pixel position; blocks the calling (background) thread until the glide lands.</summary>
    public static void GlideToBlocking(int px, int py, int durationMs = 220)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) { _win?.SetPixel(px, py); return; } // never block the UI thread

        using var done = new ManualResetEventSlim();
        d.BeginInvoke(() => { _win?.GlideTo(px, py, durationMs, done.Set); });
        done.Wait(durationMs + 400);
    }

    /// <summary>Cheap immediate reposition (used along drag paths).</summary>
    public static void JumpTo(int px, int py) => OnUi(() => _win?.SetPixel(px, py));

    /// <summary>Click flash.</summary>
    public static void Pulse() => OnUi(() => _win?.Pulse());

    private static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    private sealed class CursorWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
        private Point _from, _to;
        private DateTime _start;
        private TimeSpan _duration;
        private Action? _onDone;
        private readonly ScaleTransform _scale = new(1, 1);

        public CursorWindow()
        {
            Width = 38; Height = 38;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Left = 40; Top = 40;

            var arrow = new Path
            {
                Data = Geometry.Parse("M 2,2 L 2,26 L 9,20 L 13,30 L 17,28 L 13,18 L 22,18 Z"),
                Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xD9, 0xA2)),
                Stroke = Brushes.White,
                StrokeThickness = 1.4,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 1, Opacity = 0.6 }
            };
            var grid = new Grid { RenderTransform = _scale, RenderTransformOrigin = new Point(0.15, 0.1) };
            grid.Children.Add(arrow);
            Content = grid;

            SourceInitialized += (_, _) => MakeClickThrough(new WindowInteropHelper(this).Handle);
            _timer.Tick += OnTick;
        }

        public void SetPixel(int px, int py)
        {
            _timer.Stop();
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = px / dpi.DpiScaleX - 2;
            Top = py / dpi.DpiScaleY - 2;
        }

        public void GlideTo(int px, int py, int durationMs, Action onDone)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _from = new Point(Left, Top);
            _to = new Point(px / dpi.DpiScaleX - 2, py / dpi.DpiScaleY - 2);
            _duration = TimeSpan.FromMilliseconds(Math.Max(60, durationMs));
            _start = DateTime.UtcNow;
            _onDone = onDone;
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var t = Math.Clamp((DateTime.UtcNow - _start).TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
            var ease = t * t * (3 - 2 * t); // smoothstep
            Left = _from.X + (_to.X - _from.X) * ease;
            Top = _from.Y + (_to.Y - _from.Y) * ease;
            if (t >= 1)
            {
                _timer.Stop();
                var done = _onDone;
                _onDone = null;
                done?.Invoke();
            }
        }

        public void Pulse()
        {
            var anim = new DoubleAnimation(1.0, 1.45, TimeSpan.FromMilliseconds(110))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase()
            };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private static void MakeClickThrough(IntPtr hwnd)
        {
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
