using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The user's preferences (UI review 24), in <c>user://preferences.json</c>: UI scale (UI review 18; 0 = from the
/// screen's DPI), the plan's PDF paper, the movie resolution and the Art-Net universe offset. Not written while
/// <see cref="ViewHost.Persist"/> is off (tests), which also makes the automatic UI scale 1.
/// </summary>
internal sealed class Preferences
{
    /// <summary>Where the preferences are kept.</summary>
    private const string FilePath = "user://preferences.json";

    /// <summary>The loaded preferences (read on first use).</summary>
    private static Preferences _current;

    /// <summary>UI scale: 0 = automatic (the screen's DPI / 96), else e.g. 1.25.</summary>
    public float UiScale { get; set; }

    /// <summary>Paper of the plan's PDF: "A3" or "A4".</summary>
    public string PlanPaper { get; set; } = "A3";

    /// <summary>Movie resolution, e.g. "1920x1080".</summary>
    public string MovieResolution { get; set; } = "1920x1080";

    /// <summary>Art-Net universe received as MagicQ's universe 1 (MagicQ's default: 0).</summary>
    public int ArtNetUniverseOffset { get; set; }

    /// <summary>The UI scales offered (0 = automatic).</summary>
    internal static readonly float[] Scales = { 0f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f };

    /// <summary>Movie resolutions offered.</summary>
    internal static readonly string[] Resolutions = { "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" };

    /// <summary>The preferences (loaded from the file on first use; defaults when it is missing or unreadable).</summary>
    internal static Preferences Current => _current ??= Load();

    /// <summary>The preferences changed (saved).</summary>
    internal static event Action Changed;

    /// <summary>The automatic UI scale: the screen's DPI / 96 to a quarter (1 in tests).</summary>
    internal static float AutoScale
    {
        get
        {
            if (!ViewHost.Persist || DisplayServer.GetName() == "headless")
                return 1f;
            int dpi = DisplayServer.ScreenGetDpi(DisplayServer.WindowGetCurrentScreen());
            return Mathf.Clamp(Mathf.Round(dpi / 96f * 4f) / 4f, 1f, 2.5f);
        }
    }

    /// <summary>The UI scale in use (automatic resolved).</summary>
    internal float EffectiveScale => UiScale > 0f ? UiScale : AutoScale;

    /// <summary>Writes the preferences (not in tests) and tells who listens.</summary>
    internal void Save()
    {
        if (ViewHost.Persist)
        {
            try
            {
                File.WriteAllText(ProjectSettings.GlobalizePath(FilePath), JsonSerializer.Serialize(this, Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PrintErr($"Could not save the preferences: {e.Message}");
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Reads the file; defaults when it is missing, unreadable or in tests.</summary>
    private static Preferences Load()
    {
        string path = ProjectSettings.GlobalizePath(FilePath);
        if (!ViewHost.Persist || !File.Exists(path))
            return new Preferences();
        try
        {
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Json) ?? new Preferences();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            GD.PrintErr($"Could not read the preferences ({path}): {e.Message}");
            return new Preferences();
        }
    }

    /// <summary>JSON options of the file.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
