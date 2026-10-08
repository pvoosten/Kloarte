using System.Globalization;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Preferences (UI review 24) and the UI scale (UI review 18): File > Preferences (key map <c>preferences</c>) is a
/// dialog with the UI scale (automatic = the screen's DPI; it replaced the window stretching of before, which made the
/// UI huge in a big window and clipped it in a small one), the plan's PDF paper, the movie resolution, the Art-Net
/// universe, MagicQ's show folder (shown, with a button to open it) and the key map. Kept in
/// <c>user://preferences.json</c> (<see cref="Preferences"/>).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The preferences dialog (made on first use).</summary>
    private ConfirmationDialog _preferences;

    /// <summary>Applies the UI scale of the preferences to the window (the tool windows take it when they open).</summary>
    private void ApplyUiScale()
    {
        float scale = Preferences.Current.EffectiveScale;
        var window = GetTree().Root;
        if (!Mathf.IsEqualApprox(window.ContentScaleFactor, scale))
            window.ContentScaleFactor = scale;
        FitPanels();
    }

    /// <summary>Opens the preferences dialog with the current values; OK saves and applies them.</summary>
    private void ShowPreferences()
    {
        _preferences?.QueueFree();
        var prefs = Preferences.Current;
        _preferences = new ConfirmationDialog { Title = "Preferences", Name = "Preferences", OkButtonText = "Save" };
        var grid = new GridContainer { Columns = 2, CustomMinimumSize = new Vector2(420, 0) };
        grid.AddThemeConstantOverride("h_separation", 12);
        _preferences.AddChild(grid);

        var scale = new OptionButton { TooltipText = "Size of texts and controls; Automatic follows the screen's scaling (Windows display settings)" };
        foreach (float s in Preferences.Scales)
            scale.AddItem(s == 0f ? $"Automatic ({Preferences.AutoScale * 100:0} %)" : $"{s * 100:0} %");
        scale.Select(System.Math.Max(0, System.Array.IndexOf(Preferences.Scales, prefs.UiScale)));
        AddPreference(grid, "UI scale", scale);

        var paper = new OptionButton { TooltipText = "Paper of the plan's PDF (the plan as large as fits)" };
        paper.AddItem("A3");
        paper.AddItem("A4");
        paper.Select(prefs.PlanPaper == "A4" ? 1 : 0);
        AddPreference(grid, "Plan PDF paper", paper);

        var movie = new OptionButton { TooltipText = $"Size of a recorded movie ({MovieFps} frames per second)" };
        foreach (string r in Preferences.Resolutions)
            movie.AddItem(r.Replace("x", " x "));
        movie.Select(System.Math.Max(0, System.Array.IndexOf(Preferences.Resolutions, prefs.MovieResolution)));
        AddPreference(grid, "Movie size", movie);

        var universe = new SpinBox
        {
            MinValue = 0, MaxValue = 32767, Step = 1, Value = prefs.ArtNetUniverseOffset,
            TooltipText = "Art-Net universe received as MagicQ's universe 1 (MagicQ's default: 0); live input on UDP 6454",
        };
        AddPreference(grid, "Art-Net universe", universe);

        string folder = CurrentBuilder?.Files.ShowFolder ?? "";
        var showFolder = new Button
        {
            Text = FolderText(folder),
            TooltipText = $"{folder}\nMagicQ's own show folder (MagicQ PC always uses it); click: open it",
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            CustomMinimumSize = new Vector2(240, 0),
        };
        showFolder.Pressed += () =>
        {
            if (System.IO.Directory.Exists(folder))
                OS.ShellOpen(folder);
        };
        AddPreference(grid, "MagicQ shows", showFolder);

        var keys = new Button { Text = "Key map..." };
        KeyMap.Tooltip(keys, "Every key of Kloarte, to change in its own window", "view_keymap");
        keys.Pressed += () => KeyMap.Run("view_keymap");
        AddPreference(grid, "Keys", keys);

        _preferences.Confirmed += () =>
        {
            prefs.UiScale = Preferences.Scales[scale.Selected];
            prefs.PlanPaper = paper.Selected == 1 ? "A4" : "A3";
            prefs.MovieResolution = Preferences.Resolutions[movie.Selected];
            prefs.ArtNetUniverseOffset = (int)universe.Value;
            prefs.Save();
            _status.Text = $"Preferences saved (UI scale {prefs.EffectiveScale * 100:0} %)";
        };
        AddChild(_preferences);
        _preferences.PopupCentered();
    }

    /// <summary>A labelled row of the preferences dialog.</summary>
    private static void AddPreference(GridContainer grid, string label, Control control)
    {
        grid.AddChild(new Label { Text = label, TooltipText = control.TooltipText, MouseFilter = Control.MouseFilterEnum.Pass });
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(control);
    }

    /// <summary>The preferences changed: the UI scale and the live input's universe follow.</summary>
    private void OnPreferencesChanged()
    {
        ApplyUiScale();
        if (_artNet != null)
            _artNet.UniverseOffset = Preferences.Current.ArtNetUniverseOffset;
    }

    /// <summary>The preferences of this panel as text (tests).</summary>
    internal string PreferencesText => string.Join(", ", new[]
    {
        $"scale {Preferences.Current.EffectiveScale.ToString(CultureInfo.InvariantCulture)}",
        $"paper {Preferences.Current.PlanPaper}",
        $"movie {Preferences.Current.MovieResolution}",
        $"universe {Preferences.Current.ArtNetUniverseOffset}",
    }.Where(t => t.Length > 0));
}
