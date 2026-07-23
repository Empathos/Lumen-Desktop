using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Lumen.App.Audio;
using Lumen.App.Desktop;
using Lumen.Core.Realtime;
using Lumen.Core.Tools;

namespace Lumen.App;

public partial class MainWindow : Window
{
    private IVoiceSession? _client;
    private AudioLoop? _audio;
    private VideoStreamer? _video;
    private bool _running;

    private TextBlock? _currentUserBlock;
    private TextBlock? _currentAssistantBlock;
    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Acrylic.TryEnable(_hwnd, AlphaFromSlider(OpacitySlider.Value));
        };
    }

    private static byte AlphaFromSlider(double v) => (byte)Math.Clamp(v * 255, 8, 255);

    // ---------- session lifecycle ----------

    private async void OnToggleStartStop(object sender, RoutedEventArgs e)
    {
        if (_running) { await StopAsync(); return; }

        var provider = (Environment.GetEnvironmentVariable("LUMEN_PROVIDER") ?? "openai").Trim().ToLowerInvariant();
        var useGemini = provider is "gemini" or "google";

        var apiKey = useGemini ? ApiKeyLocator.ResolveGemini() : ApiKeyLocator.Resolve();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus(useGemini
                ? "no key: set GEMINI_API_KEY or create gemini.key"
                : "no key: set OPENAI_API_KEY or create openai.key");
            return;
        }

        try
        {
            StartStopButton.IsEnabled = false;
            SetStatus("connecting…");

            var actions = new WindowsDesktopActions();
            var tools = new ToolRegistry();

            tools.Register(new ToolDefinition
            {
                Name = "launch_app",
                Description = "Launch an application on the user's Windows desktop by name, e.g. 'chrome', 'notepad', 'ms-settings:'.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"name":{"type":"string","description":"Executable name, app alias, or path"}},"required":["name"]}
                    """,
                Handler = (args, ct) => actions.LaunchAppAsync(args.GetProperty("name").GetString() ?? "", ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "focus_window",
                Description = "Bring the first window whose title contains the given text to the foreground.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"title_contains":{"type":"string"}},"required":["title_contains"]}
                    """,
                Handler = (args, ct) => actions.FocusWindowAsync(args.GetProperty("title_contains").GetString() ?? "", ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "list_windows",
                Description = "List the titles of all visible top-level windows on the desktop.",
                ParametersJsonSchema = """{"type":"object","properties":{}}""",
                Handler = async (_, ct) =>
                {
                    var titles = await actions.ListWindowTitlesAsync(ct);
                    return JsonSerializer.Serialize(titles);
                }
            });

            tools.Register(new ToolDefinition
            {
                Name = "look_at_screen",
                Description = "Capture the whole desktop and see it as an image. Use this to orient — to find windows, icons, and controls, or to check the result of an action. For precise clicking, follow up with read_screen_elements + click_element.",
                ParametersJsonSchema = """{"type":"object","properties":{}}""",
                Handler = async (_, ct) =>
                {
                    var cap = await Task.Run(() => ScreenCapture.CaptureVirtualDesktop(), ct);
                    if (_client is not null)
                        await _client.SendImageAsync(cap.JpegBase64, "Current desktop screenshot.", ct);
                    Dispatcher.Invoke(() => AppendAction("look_at_screen"));
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        note = "Screenshot added to the conversation.",
                        desktop = new { width = cap.TrueWidth, height = cap.TrueHeight, originX = cap.OriginX, originY = cap.OriginY },
                        image_scale = cap.Scale,
                        coordinate_hint = "Elements you see are in the scaled image; multiply any pixel you read off the image by 1/image_scale to get true desktop coordinates for mouse_click. Prefer click_element when possible."
                    });
                }
            });

            tools.Register(new ToolDefinition
            {
                Name = "read_screen_elements",
                Description = "Get an indexed map of interactive UI elements (buttons, fields, links…) in the currently focused window. Call this before clicking anything.",
                ParametersJsonSchema = """{"type":"object","properties":{}}""",
                Handler = (_, ct) => actions.GetScreenElementsAsync(ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "click_element",
                Description = "Activate an element by its id from read_screen_elements. Prefers invisible background activation (does not move the user's mouse).",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"id":{"type":"string","description":"Element id like 'e12'"}},"required":["id"]}
                    """,
                Handler = (args, ct) => actions.ClickElementAsync(args.GetProperty("id").GetString() ?? "", ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "set_element_value",
                Description = "Set a text field's or slider's value directly by element id — background, no cursor, no keyboard focus. Preferred way to fill fields; if it fails, click the field and use type_text.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"id":{"type":"string","description":"Element id like 'e12'"},"value":{"type":"string"}},"required":["id","value"]}
                    """,
                Handler = (args, ct) => actions.SetElementValueAsync(
                    args.GetProperty("id").GetString() ?? "",
                    args.GetProperty("value").GetString() ?? "", ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "scroll_element",
                Description = "Scroll the list/pane containing an element id — background, no cursor. direction up/down/left/right, amount = pages (default 1).",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"id":{"type":"string","description":"Element id like 'e12'"},"direction":{"type":"string","enum":["up","down","left","right"]},"amount":{"type":"integer"}},"required":["id","direction"]}
                    """,
                Handler = (args, ct) => actions.ScrollElementAsync(
                    args.GetProperty("id").GetString() ?? "",
                    args.GetProperty("direction").GetString() ?? "down",
                    args.TryGetProperty("amount", out var am) ? am.GetInt32() : 1, ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "mouse_click",
                Description = "Click at screen coordinates with Lumen's own pointer; the user's cursor is borrowed for an instant and put back. Fallback when no element id is available.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"},"button":{"type":"string","enum":["left","right","middle"]},"clicks":{"type":"integer","description":"1 or 2 for double-click"}},"required":["x","y"]}
                    """,
                Handler = (args, ct) => actions.MouseClickAsync(
                    args.GetProperty("x").GetInt32(),
                    args.GetProperty("y").GetInt32(),
                    args.TryGetProperty("button", out var b) ? b.GetString() ?? "left" : "left",
                    args.TryGetProperty("clicks", out var c) ? c.GetInt32() : 1, ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "mouse_drag",
                Description = "Drag with the left button held from one screen coordinate to another (move windows, select text, sliders). The user's cursor is returned afterwards.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"from_x":{"type":"integer"},"from_y":{"type":"integer"},"to_x":{"type":"integer"},"to_y":{"type":"integer"}},"required":["from_x","from_y","to_x","to_y"]}
                    """,
                Handler = (args, ct) => actions.MouseDragAsync(
                    args.GetProperty("from_x").GetInt32(), args.GetProperty("from_y").GetInt32(),
                    args.GetProperty("to_x").GetInt32(), args.GetProperty("to_y").GetInt32(), ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "type_text",
                Description = "Type text into whatever currently has keyboard focus. Click the target field first.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"text":{"type":"string"},"press_enter":{"type":"boolean"}},"required":["text"]}
                    """,
                Handler = (args, ct) => actions.TypeTextAsync(
                    args.GetProperty("text").GetString() ?? "",
                    args.TryGetProperty("press_enter", out var pe) && pe.GetBoolean(), ct)
            });

            tools.Register(new ToolDefinition
            {
                Name = "press_keys",
                Description = "Press a key or shortcut combo, e.g. 'enter', 'ctrl+t', 'alt+f4', 'win+r', 'ctrl+shift+v'.",
                ParametersJsonSchema = """
                    {"type":"object","properties":{"combo":{"type":"string"}},"required":["combo"]}
                    """,
                Handler = (args, ct) => actions.PressKeysAsync(args.GetProperty("combo").GetString() ?? "", ct)
            });

            _client = useGemini
                ? new GeminiLiveClient(new GeminiLiveOptions { ApiKey = apiKey }, tools)
                : new RealtimeClient(new RealtimeClientOptions { ApiKey = apiKey }, tools);
            _audio = new AudioLoop(_client.InputSampleRate);

            // model events → UI thread
            _client.UserTranscript += (text, final) => Dispatcher.Invoke(() => AppendUser(text, final));
            _client.AssistantTranscript += delta => Dispatcher.Invoke(() => AppendAssistant(delta));
            _client.ToolInvoked += (name, _) => Dispatcher.Invoke(() => AppendAction(name));
            _client.Status += s => Dispatcher.Invoke(() => SetStatus(s));
            _client.AudioOut += pcm => _audio?.Play(pcm);
            _client.SpeechStarted += () =>
            {
                _audio?.ClearPlayback(); // barge-in: kill assistant audio instantly
                Dispatcher.Invoke(() => _currentAssistantBlock = null);
            };

            // mic → model
            _audio.MicChunk += chunk => _ = _client.SendAudioAsync(chunk);

            // instant local barge-in: kill her audio the moment you speak over her
            _audio.UserBargeIn += () =>
            {
                _audio?.ClearPlayback();
                _ = _client?.CancelResponseAsync();
                Dispatcher.Invoke(() => _currentAssistantBlock = null);
            };

            await _client.ConnectAsync();
            _audio.Start();

            // Streaming providers (Gemini) get continuous ambient sight of the desktop.
            if (_client.SupportsVideoStream)
            {
                _video = new VideoStreamer(_client, fps: 1);
                _video.Start();
            }

            AgentCursor.Show();

            _running = true;
            StartStopButton.Content = "stop";
            StartStopButton.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xE0, 0x50, 0x50));
            SetStatus(useGemini ? "live (gemini • streaming sight)" : "live (openai)");
        }
        catch (Exception ex)
        {
            SetStatus($"failed: {ex.Message}");
            await StopAsync();
        }
        finally
        {
            StartStopButton.IsEnabled = true;
        }
    }

    private async Task StopAsync()
    {
        _running = false;
        AgentCursor.Hide();
        _video?.Dispose();
        _video = null;
        _audio?.Stop();
        _audio?.Dispose();
        _audio = null;
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        StartStopButton.Content = "start";
        StartStopButton.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x41, 0xC6, 0x7B));
        SetStatus("idle");
    }

    private void OnToggleMute(object sender, RoutedEventArgs e)
    {
        if (_audio is null) return;
        _audio.Muted = !_audio.Muted;
        MuteButton.Content = _audio.Muted ? "unmute" : "mute";
    }

    // ---------- transcript rendering ----------

    private void AppendUser(string text, bool final)
    {
        if (_currentUserBlock is null)
        {
            _currentUserBlock = NewBlock("#DDE6EAEF");
            TranscriptPanel.Children.Add(_currentUserBlock);
        }
        if (final)
        {
            _currentUserBlock.Text = "you  " + text;
            _currentUserBlock = null;
        }
        else
        {
            _currentUserBlock.Text += _currentUserBlock.Text.Length == 0 ? "you  " + text : text;
        }
        ScrollToEnd();
    }

    private void AppendAssistant(string delta)
    {
        if (_currentAssistantBlock is null)
        {
            _currentAssistantBlock = NewBlock("#9CC7F5CF");
            _currentAssistantBlock.Text = "lumen  ";
            TranscriptPanel.Children.Add(_currentAssistantBlock);
        }
        _currentAssistantBlock.Text += delta;
        ScrollToEnd();
    }

    private void AppendAction(string toolName)
    {
        var block = NewBlock("#7788AACF");
        block.Text = "⚙ " + toolName;
        block.FontSize = 10.5;
        TranscriptPanel.Children.Add(block);
        _currentAssistantBlock = null; // next speech starts a fresh line
        ScrollToEnd();
    }

    private static TextBlock NewBlock(string hex) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Margin = new Thickness(0, 2, 0, 2),
        Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!
    };

    private void ScrollToEnd() => TranscriptScroll.ScrollToEnd();

    private static readonly string StatusLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumen", "status.log");

    // The status label is a single overwriting line; the log keeps transient
    // errors (api error → server closed) recoverable after the fact.
    private void SetStatus(string s)
    {
        StatusText.Text = s;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StatusLogPath)!);
            System.IO.File.AppendAllText(StatusLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.f} {s}{Environment.NewLine}");
        }
        catch { /* logging must never break the UI */ }
    }

    // ---------- chrome ----------

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // The visible "glass" is the DWM acrylic backdrop, so retint it live;
        // the WPF border stays a faint wash on top for text contrast.
        if (_hwnd != IntPtr.Zero) Acrylic.SetTint(_hwnd, AlphaFromSlider(e.NewValue));
        if (GlassBrush is not null)
            GlassBrush.Color = Color.FromArgb((byte)Math.Clamp(e.NewValue * 90, 8, 90), 0x10, 0x14, 0x18);
    }

    private async void OnClose(object sender, RoutedEventArgs e)
    {
        await StopAsync();
        Close();
    }
}
