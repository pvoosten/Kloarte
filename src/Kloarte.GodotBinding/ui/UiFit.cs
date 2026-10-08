using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Makes side-panel controls fit narrow windows and usable with the mouse wheel. Applied to every control that enters
/// a panel (<see cref="Watch"/>):
///  - buttons, option buttons and labels in a column clip their text with an ellipsis (full text in the tooltip), so a
///    long fixture type, truss or cue name can't widen the panel past the window edge;
///  - sliders and spin boxes change with the wheel while the pointer is over them (no click needed): about 1 % of the
///    range per notch for sliders, ten steps for spin boxes; Shift = fine, Ctrl = coarse. The wheel is then not passed
///    on, so the panel doesn't scroll at the same time; over labels and gaps the wheel scrolls the panel.
/// </summary>
public static class UiFit
{
    /// <summary>Applies the rules to `panel` and everything added to it later.</summary>
    public static void Watch(Control panel)
    {
        Apply(panel);
        panel.GetTree().NodeAdded += node =>
        {
            if (node is Control c && GodotObject.IsInstanceValid(panel) && panel.IsAncestorOf(c))
                Fit(c);
        };
    }

    /// <summary>Applies the rules to a control and its children.</summary>
    private static void Apply(Node node)
    {
        if (node is Control c)
            Fit(c);
        foreach (Node child in node.GetChildren())
            Apply(child);
    }

    /// <summary>Applies the rules to one control (once).</summary>
    private static void Fit(Control c)
    {
        if (c.HasMeta(Done))
            return;
        c.SetMeta(Done, true);
        bool inColumn = c.GetParent() is VBoxContainer or PanelContainer or ScrollContainer or TabContainer;
        bool expands = (c.SizeFlagsHorizontal & Control.SizeFlags.Expand) != 0;
        switch (c)
        {
            case OptionButton o:
                o.FitToLongestItem = false;
                o.ClipText = true;
                o.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                break;
            case Button b when inColumn || expands:
                b.ClipText = true;
                b.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                if (string.IsNullOrEmpty(b.TooltipText))
                    b.TooltipText = b.Text;
                break;
            case Label l when (inColumn || expands || l.CustomMinimumSize.X > 0) && l.AutowrapMode == TextServer.AutowrapMode.Off:
                l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                break;
            case Slider s:
                s.Scrollable = false;
                s.GuiInput += e => Wheel(s, e, SliderNotch(s));
                break;
            case SpinBox spin:
                spin.GetLineEdit().GuiInput += e => Wheel(spin, e, spin.Step * 10);
                break;
        }
    }

    /// <summary>
    /// A path that fits `width` with this font: whole, or without its leading folders ("...\Projects\Kruimels"; at
    /// least the last one). A path cut at the end would hide the folders that tell where it is.
    /// </summary>
    internal static string FitPathStart(string path, Font font, int fontSize, float width)
    {
        bool Fits(string text) => font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X <= width;
        if (Fits(path))
            return path;
        string[] parts = path.Split('\\');
        for (int i = 1; i < parts.Length - 1; i++)
        {
            string text = @"...\" + string.Join('\\', parts[i..]);
            if (Fits(text))
                return text;
        }
        return @"...\" + parts[^1];
    }

    /// <summary>
    /// Shows `path` on a flat button trimmed at the start to the button's width (<see cref="FitPathStart"/>), again
    /// whenever the button is resized; the full path is the tooltip's first line.
    /// </summary>
    internal static void ShowPath(Button button, string path)
    {
        button.SetMeta(PathMeta, path);
        if (!button.HasMeta(Done + "_path"))
        {
            button.SetMeta(Done + "_path", true);
            button.Resized += () => FitPath(button);
        }
        FitPath(button);
    }

    /// <summary>Fits a <see cref="ShowPath"/> button's text to its width.</summary>
    private static void FitPath(Button button)
    {
        string path = button.GetMeta(PathMeta, "").AsString();
        var style = button.GetThemeStylebox("normal");
        float width = button.Size.X - style.ContentMarginLeft - style.ContentMarginRight - 2;
        button.Text = width <= 0 ? path
            : FitPathStart(path, button.GetThemeFont("font"), button.GetThemeFontSize("font_size"), width);
    }

    /// <summary>Meta of a <see cref="ShowPath"/> button: the full path text.</summary>
    private static readonly StringName PathMeta = "ui_fit_path";

    /// <summary>Marker meta: the rules were applied.</summary>
    private static readonly StringName Done = "ui_fit";

    /// <summary>Wheel step of a slider: about 1 % of its range, at least one step.</summary>
    private static double SliderNotch(Range r) => Mathf.Max(r.Step, (r.MaxValue - r.MinValue) / 100.0);

    /// <summary>A wheel notch over a slider / spin box changes its value by `notch` and stops the event there.</summary>
    private static void Wheel(Range r, InputEvent e, double notch)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb
            || mb.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown) || !IsEditable(r))
            return;
        double k = mb.ShiftPressed ? 0.1 : mb.CtrlPressed ? 10 : 1;
        double n = notch * k * (mb.Factor > 0 ? mb.Factor : 1);
        if (r.Step > 0)
            n = Mathf.Max(n, r.Step);
        r.Value += mb.ButtonIndex == MouseButton.WheelUp ? n : -n;
        r.AcceptEvent();
    }

    /// <summary>Whether the user may change the value.</summary>
    private static bool IsEditable(Range r) => r switch
    {
        Slider s => s.Editable,
        SpinBox s => s.Editable,
        _ => true,
    };
}
