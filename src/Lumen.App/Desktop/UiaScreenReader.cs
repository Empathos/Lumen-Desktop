using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;

namespace Lumen.App.Desktop;

/// <summary>
/// UIA-first observation (LD-009): walks the focused window's accessibility
/// tree and produces a compact indexed element map the model can act on by
/// reference — no pixel hunting. Elements are cached by id so click_element
/// can use UIA patterns (background, cursor untouched) with a real-cursor
/// SendInput fallback.
/// </summary>
internal sealed class UiaScreenReader
{
    private readonly Dictionary<string, AutomationElement> _cache = new();
    private const int MaxElements = 160;
    private const int MaxDepth = 10;

    public string BuildMap()
    {
        _cache.Clear();
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "no focused window";

        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch (Exception ex) { return $"cannot read window: {ex.Message}"; }

        var sb = new StringBuilder();
        sb.Append("window: ").AppendLine(Safe(() => root.Current.Name, "?"));
        var count = 0;
        Walk(root, 0, sb, ref count);
        if (count == 0) sb.AppendLine("(no accessible elements — this app may need the screenshot fallback)");
        if (count >= MaxElements) sb.AppendLine($"(truncated at {MaxElements} elements)");
        return sb.ToString();
    }

    public string ClickById(string id)
    {
        if (!_cache.TryGetValue(id, out var el))
            return JsonSerializer.Serialize(new { ok = false, error = $"unknown element id '{id}' — call read_screen_elements first" });

        try
        {
            GlideTo(el);

            // Background paths first: no cursor movement.
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
            {
                ((InvokePattern)inv).Invoke();
                return Ok(el, "invoke");
            }
            if (el.TryGetCurrentPattern(TogglePattern.Pattern, out var tog))
            {
                ((TogglePattern)tog).Toggle();
                return Ok(el, "toggle");
            }
            if (el.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel))
            {
                ((SelectionItemPattern)sel).Select();
                return Ok(el, "select");
            }
            if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var exp))
            {
                var p = (ExpandCollapsePattern)exp;
                if (p.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) p.Expand(); else p.Collapse();
                return Ok(el, "expand/collapse");
            }

            // Fallback: real-cursor click at the element's center.
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) return JsonSerializer.Serialize(new { ok = false, error = "element has no bounds" });
            var cx = (int)(r.X + r.Width / 2);
            var cy = (int)(r.Y + r.Height / 2);
            if (!InputInjector.MouseClick(cx, cy, "left", 1))
            {
                // Loan refused (user busy) — try a posted background click (LD-029):
                // button messages straight to the hosting hwnd, no cursor at all.
                if (TryPostedClick(el, cx, cy))
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        via = "posted-click",
                        element = Safe(() => el.Current.Name, "?"),
                        note = "sent without the cursor because the user is using the mouse; unverified — re-read screen elements to confirm it took effect"
                    });
                return JsonSerializer.Serialize(new { ok = false, error = "user is actively using the mouse; not acting to avoid disrupting them — tell them briefly and retry in a moment" });
            }
            return Ok(el, "cursor-click");
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Set a field's value directly (LD-029) — no cursor, no keyboard focus.</summary>
    public string SetValueById(string id, string value)
    {
        if (!_cache.TryGetValue(id, out var el))
            return JsonSerializer.Serialize(new { ok = false, error = $"unknown element id '{id}' — call read_screen_elements first" });

        try
        {
            GlideTo(el);
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                ((ValuePattern)vp).SetValue(value);
                return Ok(el, "value-set");
            }
            if (el.TryGetCurrentPattern(RangeValuePattern.Pattern, out var rvp) && double.TryParse(value, out var num))
            {
                ((RangeValuePattern)rvp).SetValue(num);
                return Ok(el, "range-set");
            }
            return JsonSerializer.Serialize(new { ok = false, error = "element accepts no direct value — click it, then use type_text" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Scroll a container by element id (LD-029) — no cursor. The
    /// scrollable pane is often an ancestor of the mapped element, so walk up.</summary>
    public string ScrollById(string id, string direction, int amount)
    {
        if (!_cache.TryGetValue(id, out var el))
            return JsonSerializer.Serialize(new { ok = false, error = $"unknown element id '{id}' — call read_screen_elements first" });

        try
        {
            var target = el;
            object? pat = null;
            for (var i = 0; target is not null && i < 6; i++)
            {
                if (target.TryGetCurrentPattern(ScrollPattern.Pattern, out pat)) break;
                target = TreeWalker.ControlViewWalker.GetParent(target);
            }
            if (pat is not ScrollPattern sp || target is null)
                return JsonSerializer.Serialize(new { ok = false, error = "nothing scrollable at or above this element — click the area and use press_keys pagedown/pageup" });

            var (h, v) = direction.ToLowerInvariant() switch
            {
                "up" => (ScrollAmount.NoAmount, ScrollAmount.LargeDecrement),
                "down" => (ScrollAmount.NoAmount, ScrollAmount.LargeIncrement),
                "left" => (ScrollAmount.LargeDecrement, ScrollAmount.NoAmount),
                "right" => (ScrollAmount.LargeIncrement, ScrollAmount.NoAmount),
                _ => (ScrollAmount.NoAmount, ScrollAmount.NoAmount)
            };
            if (h == ScrollAmount.NoAmount && v == ScrollAmount.NoAmount)
                return JsonSerializer.Serialize(new { ok = false, error = "direction must be up, down, left or right" });

            GlideTo(target);
            var steps = Math.Clamp(amount, 1, 10);
            for (var i = 0; i < steps; i++) sp.Scroll(h, v);
            return Ok(target, $"scroll-{direction.ToLowerInvariant()}x{steps}");
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Lumen's pointer glides to the target so her act is visible;
    /// the real cursor stays untouched on all background paths.</summary>
    private static void GlideTo(AutomationElement el)
    {
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) return;
            AgentCursor.GlideToBlocking((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            AgentCursor.Pulse();
        }
        catch { /* visual only */ }
    }

    private static bool TryPostedClick(AutomationElement el, int screenX, int screenY)
    {
        try
        {
            var cur = el;
            var hwnd = IntPtr.Zero;
            for (var i = 0; cur is not null && i < 8; i++)
            {
                var handle = cur.Current.NativeWindowHandle;
                if (handle != 0) { hwnd = new IntPtr(handle); break; }
                cur = TreeWalker.ControlViewWalker.GetParent(cur);
            }
            if (hwnd == IntPtr.Zero) return false;

            var pt = new POINT { X = screenX, Y = screenY };
            if (!ScreenToClient(hwnd, ref pt)) return false;
            var lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
            PostMessage(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lParam);
            PostMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, lParam);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Walk(AutomationElement parent, int depth, StringBuilder sb, ref int count)
    {
        if (depth >= MaxDepth || count >= MaxElements) return;

        AutomationElement? child;
        try { child = TreeWalker.ControlViewWalker.GetFirstChild(parent); }
        catch { return; }

        while (child is not null && count < MaxElements)
        {
            try
            {
                var cur = child.Current;
                if (!cur.IsOffscreen)
                {
                    var name = cur.Name ?? "";
                    var type = cur.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "?";
                    var interesting = IsInteresting(type) || !string.IsNullOrWhiteSpace(name);
                    if (interesting)
                    {
                        count++;
                        var id = $"e{count}";
                        _cache[id] = child;
                        var r = cur.BoundingRectangle;
                        if (name.Length > 60) name = name[..60] + "…";
                        sb.Append(id).Append(' ').Append(type);
                        if (name.Length > 0) sb.Append(" \"").Append(name).Append('"');
                        if (!r.IsEmpty) sb.Append(" @").Append((int)(r.X + r.Width / 2)).Append(',').Append((int)(r.Y + r.Height / 2));
                        if (!cur.IsEnabled) sb.Append(" [disabled]");
                        sb.AppendLine();
                    }
                    Walk(child, depth + 1, sb, ref count);
                }
            }
            catch
            {
                // stale element mid-walk; skip
            }

            try { child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
            catch { break; }
        }
    }

    private static bool IsInteresting(string type) => type is
        "Button" or "Edit" or "ComboBox" or "CheckBox" or "RadioButton" or
        "MenuItem" or "ListItem" or "TreeItem" or "TabItem" or "Hyperlink" or
        "SplitButton" or "Slider" or "Document";

    private static string Ok(AutomationElement el, string via) =>
        JsonSerializer.Serialize(new { ok = true, via, element = Safe(() => el.Current.Name, "?") });

    private static string Safe(Func<string?> f, string fallback)
    {
        try { return f() ?? fallback; } catch { return fallback; }
    }

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);
}
