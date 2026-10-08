using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The Look of the room (UI review 24), saved with the project (<see cref="ProjectLook"/> in the project document): the
/// stray light (the <see cref="StrayLight"/> nodes) and the haze density (the global shader uniform
/// <c>haze_density</c> of the beams and the environment's volumetric fog, <see cref="HazeControl"/>). Edited in the
/// inspector when nothing is selected; a change is an unsaved "look" of the project (not an undo step: undo covers show
/// data only). The Haze slider is the haze of the room before any cue sets smoke (the Playback tab's smoke per cue,
/// <see cref="Kloarte.Model.CueSmoke"/>, takes over from there).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The haze density set last (tests; the cues' smoke too).</summary>
    internal float Haze => HazeControl.Current;

    /// <summary>Puts the open project's look on the stage (after every show / project change).</summary>
    private void ApplyLook()
    {
        if (CurrentBuilder?.Files.Look is not { } look)
            return;
        SetStrayLight(look.StrayLight);
        SetHaze(look.Haze);
    }

    /// <summary>Sets the stray light of every <see cref="StrayLight"/> node of the scene.</summary>
    private void SetStrayLight(float amount)
    {
        foreach (var stray in GetTree().Root.FindChildren("*", "Node", true, false).OfType<StrayLight>())
            stray.Amount = amount;
    }

    /// <summary>Sets the haze density of the beams and of the environment's volumetric fog.</summary>
    private void SetHaze(float density) => HazeControl.Set(GetTree(), density);

    /// <summary>The Look's sliders (stray light, haze) for the inspector; they write the project's look.</summary>
    private void LookRows()
    {
        var look = CurrentBuilder?.Files.Look;
        var current = GetTree().Root.FindChildren("*", "Node", true, false).OfType<StrayLight>().FirstOrDefault();
        _params.AddChild(SliderRow("Stray light",
            "Spill, haze scatter and bounce that keep the room from going fully dark (0 = pitch black around the beams); " +
            "saved with the project",
            0, 2, 0.01, look?.StrayLight ?? current?.Amount ?? ProjectLook.DefaultStrayLight,
            v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), null, v =>
            {
                if (look != null)
                    look.StrayLight = (float)v;
                SetStrayLight((float)v);
            }).Row);
        float haze = look?.Haze ?? HazeControl.Current;
        _params.AddChild(SliderRow("Haze",
            "Haze density in the beams (0 % = clean air, 100 % = a thick haze); saved with the project",
            0, ProjectLook.MaxHaze * 1000, 1, haze * 1000, v => $"{v:0} %", null, v =>
            {
                if (look != null)
                    look.Haze = (float)(v / 1000);
                SetHaze((float)(v / 1000));
                _player.Refresh();   // before the first cue that sets smoke, this is the haze of the show
            }).Row);
    }
}
