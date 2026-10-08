using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The caption of an export (UI review 32): the cue at the time shown (number and text) and, in an export for one
/// person, their name; bottom left on a dark band, its size following the picture. Used by the movie process and the
/// stills / contact sheet of the app.
/// </summary>
public partial class ExportCaption : Control
{
    /// <summary>The show's timeline (the cue at a time).</summary>
    public PlaybackTimeline Timeline { get; set; }

    /// <summary>The person the export is for, or null.</summary>
    public string ActorName { get; set; }

    /// <summary>Show the cue (else only the person's name).</summary>
    public bool ShowCue { get; set; } = true;

    /// <summary>The caption's text.</summary>
    private Label _label;

    /// <inheritdoc />
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label
        {
            AnchorLeft = 0, AnchorTop = 1, AnchorRight = 0, AnchorBottom = 1,
            GrowVertical = GrowDirection.Begin,
            Modulate = Colors.White,
        };
        _label.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.6f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 6,
        });
        AddChild(_label);
        Resized += Fit;
        Fit();
    }

    /// <summary>Shows the caption for time `t` of the timeline.</summary>
    public void ShowAt(double t)
    {
        if (_label == null)
            return;
        string cue = "";
        if (ShowCue && Timeline != null)
        {
            int i = Timeline.SegmentAt(t);
            cue = i < 0 ? "Before the first cue" : $"Cue {Timeline.Segments[i].CueNumber}  {Timeline.Segments[i].Text}".TrimEnd();
        }
        string text = string.Join("   ·   ", new[] { ActorName, cue }.Where(s => !string.IsNullOrEmpty(s)));
        _label.Text = text;
        _label.Visible = text.Length > 0;
    }

    /// <summary>The text shown (tests).</summary>
    internal string Text => _label?.Text ?? "";

    /// <summary>Font size and margins follow the picture's height.</summary>
    private void Fit()
    {
        if (_label == null)
            return;
        float h = Size.Y > 0 ? Size.Y : 720;
        _label.AddThemeFontSizeOverride("font_size", Mathf.Max(14, Mathf.RoundToInt(h / 28f)));
        _label.OffsetLeft = h / 40f;
        _label.OffsetBottom = -h / 40f;
    }

    /// <summary>
    /// For an export for one person: only that actor keeps their name on the shirt (the others wear plain shirts).
    /// Returns the person's name, or null when the show has no such actor.
    /// </summary>
    public static string ShowOnlyActor(ShowBuilder builder, string actorId)
    {
        var def = builder.Model?.Blocking.Find(actorId);
        if (def == null)
            return null;
        foreach (var (id, actor) in builder.Actors)
            actor.SetLabel(id == actorId ? def.Name : "");
        return def.Name;
    }

    /// <summary>Every actor wears their own name again (after an export for one person).</summary>
    public static void ShowAllActors(ShowBuilder builder)
    {
        foreach (var (id, actor) in builder.Actors)
            if (builder.Model?.Blocking.Find(id) is { } def)
                actor.SetLabel(def.Name);
    }
}
