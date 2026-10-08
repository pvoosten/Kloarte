using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the smoke (haze) per cue, in the Playback tab (TODO 12 Oct 2026, decided with the user: Kloarte's own
/// data in the project, like the actors' blocking; a hazer takes its time to build). The current cue can set the amount
/// (0..100 % of the densest haze) and the seconds the haze takes to get there; a cue that doesn't, leaves it as it is
/// (tracking). The haze of the room follows (<see cref="CuePlayer"/>, <see cref="HazeControl"/>); the real hazer is
/// still programmed in MagicQ. Edits are undoable blocking edits.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The smoke rows: title, "this cue sets the smoke", amount, time, and what the room has now.</summary>
    private Label _smokeTitle, _smokeInfo;
    private CheckBox _smokeSets;
    private HSlider _smokeAmount;
    private SpinBox _smokeTime;

    /// <summary>The controls are being set from the cue (no edit).</summary>
    private bool _smokeSyncing;

    /// <summary>The smoke rows of the Playback tab.</summary>
    private Control SmokeRows()
    {
        var box = new VBoxContainer { Name = "Smoke" };
        box.AddChild(new HSeparator());
        _smokeTitle = new Label { Name = "SmokeTitle", Modulate = new Color(1f, 0.75f, 0.4f) };
        box.AddChild(_smokeTitle);
        _smokeSets = new CheckBox
        {
            Text = "This cue sets the smoke",
            TooltipText = "Off: this cue leaves the smoke as it is (it tracks, like the lights). On: from this cue's GO the haze " +
                          "builds or thins to the amount below. Saved in the project (blocking/steps.yaml); the real hazer is " +
                          "programmed in MagicQ",
        };
        _smokeSets.Toggled += on => EditSmoke(smoke => on ? smoke ?? new CueSmoke { Amount = _smokeAmount.Value } : null);
        box.AddChild(_smokeSets);
        var (amountRow, amount) = SliderRow("Amount",
            "Haze in the beams after this cue, in % of the densest haze (the Haze slider's scale: 0 % = clean air)",
            0, 100, 1, 0, v => $"{v:0} %", null, v =>
            {
                if (!_smokeSyncing)
                    EditSmoke(smoke => smoke == null ? null : new CueSmoke { Amount = Math.Round(v, 1), Time = smoke.Time });
            });
        _smokeAmount = amount;
        box.AddChild(amountRow);
        _smokeTime = new SpinBox
        {
            MinValue = 0, MaxValue = 3600, Step = 1, Value = Blocking.DefaultSmokeTime, Suffix = "s",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = $"Seconds the haze takes to build or thin to this amount, like a hazer (default {Blocking.DefaultSmokeTime:0} s). " +
                          "A hazer doesn't follow the cue's fade time",
        };
        _smokeTime.ValueChanged += v =>
        {
            if (!_smokeSyncing)
                EditSmoke(smoke => smoke == null ? null : new CueSmoke
                {
                    Amount = smoke.Amount,
                    Time = Math.Abs(v - Blocking.DefaultSmokeTime) < 0.05 ? null : (float)v,
                });
        };
        var timeRow = new HBoxContainer();
        timeRow.AddChild(new Label { Text = "Builds in", CustomMinimumSize = new Vector2(110, 0), TooltipText = _smokeTime.TooltipText });
        timeRow.AddChild(_smokeTime);
        box.AddChild(timeRow);
        _smokeInfo = new Label
        {
            Name = "SmokeInfo",
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(200, 0),
        };
        box.AddChild(_smokeInfo);
        SyncSmoke();
        return box;
    }

    /// <summary>
    /// Changes the current cue's smoke (`change` gets the cue's entry or null and returns the new one; null = none): one
    /// named undo step; the haze shows it at once.
    /// </summary>
    private void EditSmoke(Func<CueSmoke, CueSmoke> change)
    {
        var builder = CurrentBuilder;
        if (_smokeSyncing || builder?.Model == null || _player.Stack == null || _player.Step < 0)
            return;
        var smoke = builder.Model.Blocking.Smoke;
        int cue = _player.CueId;
        var updated = change(smoke.GetValueOrDefault(cue));
        builder.Files.NameNextEdit($"Smoke in cue {_player.Stack.Steps[_player.Step].CueNumberText}");
        if (updated == null)
            smoke.Remove(cue);
        else
            smoke[cue] = updated;
        _player.Invalidate();   // the timeline is laid out again (and the controls follow)
        _player.Refresh();
    }

    /// <summary>
    /// Puts the current cue's smoke in the controls: its own amount and time, or (it sets none) the amount it inherits and
    /// the controls off; before the first cue they are off.
    /// </summary>
    private void SyncSmoke()
    {
        if (_smokeSets == null)
            return;
        var builder = CurrentBuilder;
        var stack = _player.Stack;
        bool cue = builder?.Model != null && stack != null && _player.Step >= 0 && _player.Step < stack.Steps.Count;
        var blocking = builder?.Model?.Blocking;
        _smokeSyncing = true;
        var own = cue ? blocking.Smoke.GetValueOrDefault(_player.CueId) : null;
        string inherited = "";
        double amount = ProjectLook.PercentOf(builder?.Files?.Look?.Haze ?? HazeControl.Current);
        string from = "the project's haze (Look > Haze)";
        if (cue)
            for (int i = _player.Step - (own == null ? 0 : 1); i >= 0; i--)
                if (blocking.Smoke.TryGetValue(stack.Steps[i].CueId, out var earlier))
                {
                    amount = earlier.Amount;
                    from = $"cue {stack.Steps[i].CueNumberText}";
                    break;
                }
        _smokeTitle.Text = cue ? $"Smoke in cue {stack.Steps[_player.Step].CueNumberText}" : "Smoke (choose a cue)";
        _smokeSets.Disabled = !cue;
        _smokeSets.SetPressedNoSignal(own != null);
        _smokeAmount.Editable = own != null;
        // A range that is not in the tree yet (the first sync) doesn't emit value_changed, so the value text would stay at 0:
        // the signal is raised by hand (the handler ignores it while syncing).
        double shown = own?.Amount ?? amount;
        _smokeAmount.SetValueNoSignal(shown);
        _smokeAmount.EmitSignal(Godot.Range.SignalName.ValueChanged, shown);
        _smokeTime.Editable = own != null;
        _smokeTime.Value = own?.Seconds ?? Blocking.DefaultSmokeTime;
        inherited = !cue ? "Smoke is set per cue; the haze before the first cue is the project's (Look > Haze)."
            : own != null ? $"From this cue's GO the haze takes {own.Seconds:0.#} s to reach {own.Amount:0.#} %. Later cues keep it."
            : $"Leaves the smoke as it is: {amount.ToString("0.#", CultureInfo.InvariantCulture)} % from {from}.";
        _smokeInfo.Text = inherited;
        _smokeSyncing = false;
    }
}
