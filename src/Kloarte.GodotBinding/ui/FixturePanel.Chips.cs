using System.Collections.Generic;
using System.Linq;

namespace Kloarte.GodotBinding;

/// <summary>
/// Mode chips (UI review 14): the modes that change what the stage shows or what a click does are chips in the status
/// bar, each with an ✕ that ends it: the group / cue filter (unlisted lamps are off), Solo, the programmer holding
/// values, live Art-Net, placing lamps / stands in the plan and Aim.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The modes on now, as chips (refreshed every frame; the bar rebuilds only on a change).</summary>
    internal List<ModeChip> ModeChips()
    {
        var chips = new List<ModeChip>();
        if (_groupFilter?.Selected > 0)
        {
            string group = _groupFilter.GetItemMetadata(_groupFilter.Selected).AsString();
            chips.Add(new ModeChip($"Group: {group.Substring(group.IndexOf(':') + 1)}",
                "Only the lamps of this MagicQ group are listed and light; the others are off",
                () => { _groupFilter.Select(0); FillList(); }));
        }
        if (_cueFilter?.Selected > 0)
            chips.Add(new ModeChip($"Cue: {_cueFilter.GetItemText(_cueFilter.Selected).Split("  ")[0]}",
                "Only the lamps recorded in this cue are listed and light; the others are off",
                () => { _cueFilter.Select(0); FillList(); }));
        if (_solo?.ButtonPressed == true)
            chips.Add(new ModeChip("Solo", $"Only the selected lamp lights, at {SoloIntensity * 100:0} %",
                () => _solo.ButtonPressed = false));
        if (CueEditing.AimHeads is { } aiming)
            chips.Add(new ModeChip($"Aim: {aiming.Count} head{(aiming.Count == 1 ? "" : "s")}",
                "The next click on the stage points them there (pan / tilt into the current cue)", CueEditing.StopAim));
        if (_artNet?.Running == true)
            chips.Add(new ModeChip("Live Art-Net", "The stage shows what MagicQ outputs (cue playback is off)",
                () => SetLiveInput(false)));
        if (_host?.Views.OfType<PlanView>().FirstOrDefault() is { Placing: { } placing } plan)
            chips.Add(new ModeChip($"Placing: {placing}", "Each click in the plan adds one; right click or Esc stops",
                plan.StopPlacing));
        if (_aimOnce)
            chips.Add(new ModeChip("Aim", "The next click on the stage aims the selected lamps; Esc cancels",
                () => { _aimOnce = false; _status.Text = ""; }));
        return chips;
    }

    /// <summary>Shows the modes on now in the status bar.</summary>
    private void RefreshChips() => _statusBar?.SetChips(ModeChips());
}
