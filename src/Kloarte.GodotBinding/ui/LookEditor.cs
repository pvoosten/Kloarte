using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Edits the values a cue or a palette sets for heads of the native show, in physical units (decided 14 Oct 2026):
/// intensity in %, pan / tilt in degrees, colour, zoom, focus, iris, blades, and gobo / colour wheel / prism slots by
/// name; per channel whether the cue sets it (white), takes it from a palette (blue, "@name") or tracks it from an
/// earlier cue (grey), with "×" to let it track again and "@" to take it from a palette. Moving heads can be aimed at a
/// point clicked on the stage. Every change is one undo step (<see cref="CueEditing.Edit"/>).
/// </summary>
internal static class LookEditor
{
    /// <summary>Readable names of GDTF attributes (others show their key).</summary>
    private static readonly Dictionary<string, string> Names = new()
    {
        ["intensity"] = "Intensity", ["Pan"] = "Pan", ["Tilt"] = "Tilt",
        ["ColorSub_C"] = "Cyan", ["ColorSub_M"] = "Magenta", ["ColorSub_Y"] = "Yellow", ["CTO"] = "CTO", ["CTB"] = "CTB",
        ["ColorAdd_R"] = "Red", ["ColorAdd_G"] = "Green", ["ColorAdd_B"] = "Blue", ["ColorAdd_W"] = "White",
        ["ColorAdd_RY"] = "Amber", ["ColorAdd_GY"] = "Lime", ["ColorMacro1"] = "Colour wheel", ["Color1"] = "Colour wheel",
        ["Zoom"] = "Zoom", ["Focus1"] = "Focus", ["Iris"] = "Iris", ["Frost1"] = "Frost",
        ["Gobo1"] = "Gobo", ["Gobo1Pos"] = "Gobo rotation", ["Gobo2"] = "Gobo 2", ["Gobo2Pos"] = "Gobo 2 rotation",
        ["AnimationWheel1"] = "Animation wheel", ["AnimationWheel1Pos"] = "Animation rotation",
        ["Prism1"] = "Prism", ["Prism1Pos"] = "Prism rotation", ["Shutter1"] = "Shutter",
        ["Blade1A"] = "Blade 1 (top)", ["Blade1Rot"] = "Blade 1 angle", ["Blade2A"] = "Blade 2 (right)", ["Blade2Rot"] = "Blade 2 angle",
        ["Blade3A"] = "Blade 3 (bottom)", ["Blade3Rot"] = "Blade 3 angle", ["Blade4A"] = "Blade 4 (left)", ["Blade4Rot"] = "Blade 4 angle",
        ["ShaperRot"] = "Blades rotation",
    };

    /// <summary>Colours of a value's state.</summary>
    private static readonly Color Own = Colors.White, FromPalette = new(0.55f, 0.8f, 1f), Tracked = new(0.6f, 0.6f, 0.6f), Unset = new(0.45f, 0.45f, 0.45f);

    /// <summary>A value of a channel, where it comes from, and the text explaining it.</summary>
    private readonly record struct Shown(LookValue Value, LookValue Resolved, Color Color, string Where, bool InHere);

    /// <summary>
    /// The values cue `cueIndex` sets for `heads` (the first head's channels; an edit goes to every head that has the
    /// channel). `rebuild` is called (deferred by the caller) after an edit.
    /// </summary>
    internal static Control ForCue(ShowBuilder builder, int cueIndex, IReadOnlyList<int> heads, Action rebuild)
    {
        var box = new VBoxContainer { Name = "LookEditor" };
        var design = CueEditing.Design(builder);
        if (design == null || heads.Count == 0 || design.LayoutOf(heads[0]) is not { } layout)
        {
            box.AddChild(Note(design == null ? "Cue values are edited in a native project" : "Not patched"));
            return box;
        }
        if (cueIndex < 0 || cueIndex >= design.CueOrder.Count)
        {
            box.AddChild(Note("Before the first cue: choose a cue (Playback, or GO) to set values in it"));
            return box;
        }
        var cue = design.CueOrder[cueIndex];
        int head = heads[0];
        string undo(string what) => $"{what} of head{(heads.Count == 1 ? $" {head}" : "s")} in cue {cue.Cue}";
        Shown Value(string key)
        {
            var own = cue.Of(head)?.GetValueOrDefault(key);
            if (own != null)
                return own.IsPalette
                    ? new Shown(own, DesignEdits.Resolved(design, head, key, own), FromPalette, $"from palette {own.Palette}, set in this cue", true)
                    : new Shown(own, own, Own, "set in this cue", true);
            var (tracked, at) = DesignEdits.Tracked(design, cueIndex - 1, head, key);
            return tracked == null ? new Shown(null, null, Unset, "not set in any cue up to here", false)
                : new Shown(tracked, DesignEdits.Resolved(design, head, key, tracked), Tracked, $"tracks from cue {design.CueOrder[at].Cue}" +
                    (tracked.IsPalette ? $" (palette {tracked.Palette})" : ""), false);
        }
        void Set(string key, LookValue value) => CueEditing.Edit(builder, undo(Label(key)), d =>
        {
            foreach (int h in heads.Where(h => d.LayoutOf(h)?[key] != null))
            {
                // Leaving a colour wheel preset for "Raw DMX": the RGB(W) values keep its colour.
                var kept = DesignEdits.KeepColour(d.LayoutOf(h), key, value,
                    k => DesignEdits.Resolved(d, h, k, DesignEdits.Tracked(d, cueIndex, h, k).Value));
                DesignEdits.Set(cue.Heads, h, key, value);
                foreach (var (k, v) in kept)
                    DesignEdits.Set(cue.Heads, h, k, v);
            }
        });

        // The whole head from a palette, or tracking again.
        var top = new HBoxContainer();
        var palettes = design.Palettes.Where(p => p.Of(head)?.Count > 0).ToList();
        var pick = new OptionButton { TooltipText = "Take every value a palette has for this head (the cue refers to the palette)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pick.AddItem(palettes.Count == 0 ? "No palette for this head" : "Use palette...");
        foreach (var p in palettes)
            pick.AddItem(p.Name);
        pick.Disabled = palettes.Count == 0;
        pick.ItemSelected += i =>
        {
            if (i <= 0)
                return;
            var palette = palettes[(int)i - 1];
            CueEditing.Edit(builder, undo($"Palette {palette.Name}"), d =>
            {
                foreach (int h in heads)
                    foreach (string key in palette.Of(h)?.Keys ?? Enumerable.Empty<string>())
                        DesignEdits.Set(cue.Heads, h, key, LookValue.OfPalette(palette.Name));
            });
            rebuild();
        };
        var track = new Button { Text = "Track all", TooltipText = "Take every value of this head out of the cue: it tracks from the cues before" };
        track.Disabled = heads.All(h => cue.Of(h) == null);
        track.Pressed += () =>
        {
            CueEditing.Edit(builder, undo("Track"), d =>
            {
                foreach (int h in heads)
                    cue.Heads.Remove(DesignShow.Key(h));
            });
            rebuild();
        };
        top.AddChild(pick);
        top.AddChild(track);
        box.AddChild(top);

        Channels(box, builder, design, layout, head, Value, Set, key => Set(key, null), rebuild,
            layout["Pan"] != null && layout["Tilt"] != null ? heads : null);
        return box;
    }

    /// <summary>The values a palette holds for a head; `rebuild` is called (deferred by the caller) after an edit.</summary>
    internal static Control ForPalette(ShowBuilder builder, DesignPalette palette, int head, Action rebuild)
    {
        var box = new VBoxContainer { Name = "PaletteLook" };
        var design = CueEditing.Design(builder);
        if (design?.LayoutOf(head) is not { } layout)
        {
            box.AddChild(Note($"Head {head} is not patched"));
            return box;
        }
        Shown Value(string key) => palette.Of(head)?.GetValueOrDefault(key) is { } v
            ? new Shown(v, v, Own, $"in palette {palette.Name}", true)
            : new Shown(null, null, Unset, "not in the palette", false);
        void Set(string key, LookValue value) =>
            CueEditing.Edit(builder, $"{Label(key)} of head {head} in palette {palette.Name}", _ =>
            {
                // Leaving a colour wheel preset for "Raw DMX": the RGB(W) values keep its colour.
                var kept = DesignEdits.KeepColour(layout, key, value, k => palette.Of(head)?.GetValueOrDefault(k));
                DesignEdits.Set(palette.Heads, head, key, value);
                foreach (var (k, v) in kept)
                    DesignEdits.Set(palette.Heads, head, k, v);
            });
        Channels(box, builder, design, layout, head, Value, Set, key => Set(key, null), rebuild, null, references: false);
        return box;
    }

    /// <summary>
    /// The channel rows, grouped as the console does (intensity, position, colour, beam; the rest folded under "Other
    /// channels"). `aim`: the heads "Aim" points at a clicked spot (null = no Aim button); `references`: values can come
    /// from a palette (a cue's, not a palette's own).
    /// </summary>
    private static void Channels(VBoxContainer box, ShowBuilder builder, DesignShow design, HeadLayout layout, int head,
        Func<string, Shown> value, Action<string, LookValue> set, Action<string> clear, Action rebuild, IReadOnlyList<int> aim,
        bool references = true)
    {
        var known = layout.Channels.Where(c => c.Kind != HeadChannelKind.Raw || layout.Fixture == null).ToList();
        foreach (var (cls, title) in new[] { ('I', "Intensity"), ('P', "Position"), ('C', "Colour"), ('B', "Beam") })
        {
            var channels = known.Where(c => Group(c) == cls && IsMain(c)).ToList();
            if (channels.Count == 0)
                continue;
            box.AddChild(Section(title));
            foreach (var channel in channels)
                box.AddChild(Row(builder, design, channel, head, value(channel.Key), set, clear, rebuild, aim != null || references));
            if (cls == 'P' && aim != null)
            {
                var button = new Button
                {
                    Text = "Aim at the stage...",
                    TooltipText = "Then click on the stage floor or the back wall: pan and tilt go into this cue",
                };
                button.Pressed += () => CueEditing.StartAim(aim);
                box.AddChild(button);
            }
        }
        var others = known.Where(c => !IsMain(c)).ToList();
        if (others.Count == 0)
            return;
        var fold = new Button { Text = $"Other channels ({others.Count})", ToggleMode = true, Flat = true, Alignment = HorizontalAlignment.Left };
        var rest = new VBoxContainer { Visible = false };
        fold.Toggled += on => rest.Visible = on;
        foreach (var channel in others)
            rest.AddChild(Row(builder, design, channel, head, value(channel.Key), set, clear, rebuild, aim != null || references));
        box.AddChild(fold);
        box.AddChild(rest);
    }

    /// <summary>
    /// The group a channel is shown in: by its GDTF attribute when it has one (pan / tilt = position, colour attributes and
    /// CTO = colour, the rest beam), else by the fixture type's class.
    /// </summary>
    private static char Group(HeadChannel channel)
    {
        if (channel.Kind == HeadChannelKind.Intensity)
            return 'I';
        if (channel.Gdtf == null)
            return channel.Class;
        string a = channel.Gdtf.Attribute;
        return a is "Pan" or "Tilt" ? 'P'
            : a.StartsWith("Color") || a is "CTO" or "CTB" or "CTC" or "Tint" ? 'C'
            : 'B';
    }

    /// <summary>True for a channel shown in its group (intensity, a known attribute, or any channel of a head without GDTF).</summary>
    private static bool IsMain(HeadChannel channel) =>
        channel.Kind != HeadChannelKind.Gdtf || Names.ContainsKey(channel.Key);

    /// <summary>The readable name of a channel key.</summary>
    internal static string Label(string key) => Names.TryGetValue(key, out var name) ? name : key;

    /// <summary>One channel: its name, its value (number field or slot picker), "@" (a palette) and "×" (track / leave out).</summary>
    private static Control Row(ShowBuilder builder, DesignShow design, HeadChannel channel, int head, Shown shown,
        Action<string, LookValue> set, Action<string> clear, Action rebuild, bool references)
    {
        var row = new HBoxContainer { Name = "Row_" + channel.Key };
        var label = new Label
        {
            Text = Label(channel.Key),
            CustomMinimumSize = new Vector2(110, 0),
            Modulate = shown.Color,
            TooltipText = $"{channel.Key} ({channel.Name}): {shown.Where}" + (shown.Value != null ? $"\nvalue: {shown.Value}" : ""),
            MouseFilter = Control.MouseFilterEnum.Pass,
            ClipText = true,
        };
        row.AddChild(label);
        void Changed(LookValue value)
        {
            set(channel.Key, value);
            rebuild();
        }
        row.AddChild(Field(channel, shown, Changed));
        var palettes = design.Palettes.Where(p => p.Of(head)?.ContainsKey(channel.Key) == true).ToList();
        if (references && palettes.Count > 0)
        {
            var at = new MenuButton { Text = "@", TooltipText = "Take this value from a palette", Flat = false };
            foreach (var p in palettes)
                at.GetPopup().AddItem(p.Name);
            at.GetPopup().IndexPressed += i => Changed(LookValue.OfPalette(palettes[(int)i].Name));
            row.AddChild(at);
        }
        var x = new Button { Text = "×", Disabled = !shown.InHere, TooltipText = "Take it out: it tracks from the cues before (in a palette: leave it out)" };
        x.Pressed += () =>
        {
            clear(channel.Key);
            rebuild();
        };
        row.AddChild(x);
        return row;
    }

    /// <summary>The editor of a value: a number field (with the channel's unit range), or a picker of the channel's slots.</summary>
    private static Control Field(HeadChannel channel, Shown shown, Action<LookValue> changed)
    {
        var sets = channel.SetNames;
        var range = channel.Range;
        // A slot picker for wheels and channels of named settings (shutter, macros, modes); a number field for the rest.
        string attribute = channel.Gdtf?.Attribute ?? "";
        bool named = channel.Gdtf?.Functions.Any(f => f.Wheel.Length > 0) == true
                     || attribute.StartsWith("Shutter") || attribute.Contains("Macro") || attribute.Contains("Control") || attribute.Contains("Mode");
        bool continuous = channel.Kind != HeadChannelKind.Gdtf || range is { } r && Math.Abs(r.To - r.From) > 1e-6 && !(named && sets.Count > 0);
        var resolved = shown.Resolved;
        if (!continuous)
        {
            var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = shown.Color, FitToLongestItem = false };
            string current = resolved == null ? null : channel.SetOf(resolved) ?? resolved.ToString();
            if (current != null && !sets.Contains(current))
                picker.AddItem(current);
            foreach (string name in sets)
                picker.AddItem(name);
            picker.Selected = current == null ? -1 : Enumerable.Range(0, picker.ItemCount).FirstOrDefault(i => picker.GetItemText(i) == current);
            picker.ItemSelected += i =>
            {
                string name = picker.GetItemText((int)i);
                if (sets.Contains(name))
                    changed(LookValue.Of(name));
            };
            return picker;
        }
        double min = 0, max = 255, step = 1;
        string suffix = "";
        if (channel.Kind == HeadChannelKind.Intensity)
            (max, step, suffix) = (100, 0.1, "%");
        else if (channel.Kind == HeadChannelKind.Gdtf && range is { } g)
        {
            min = Math.Min(g.From, g.To);
            max = Math.Max(g.From, g.To);
            step = Nice((max - min) / 1000);
            suffix = channel.Key is "Pan" or "Tilt" or "Zoom" or "Gobo1Pos" or "ShaperRot" || channel.Key.EndsWith("Rot") ? "°" : channel.Key == "CTO" ? "K" : "";
        }
        var spin = new SpinBox
        {
            MinValue = min, MaxValue = max, Step = step, Suffix = suffix, AllowGreater = false, AllowLesser = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Modulate = shown.Color, SelectAllOnFocus = true,
        };
        double? number = resolved == null ? null : channel.NumberOf(resolved);
        spin.SetValueNoSignal(number ?? (channel.Kind == HeadChannelKind.Intensity ? 0 : min));
        if (number == null && resolved != null)
            spin.TooltipText = resolved.ToString();
        spin.ValueChanged += v => changed(LookValue.Of(Math.Round(v, Decimals(step))));
        return spin;
    }

    /// <summary>A step of 1, 2 or 5 times a power of ten.</summary>
    private static double Nice(double raw)
    {
        if (raw <= 0)
            return 0.001;
        double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double m = raw / power;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * power;
    }

    /// <summary>The decimals of a step.</summary>
    private static int Decimals(double step) => Math.Clamp((int)Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 6);

    /// <summary>A section title.</summary>
    private static Label Section(string text) => new() { Text = text, Modulate = new Color(1f, 0.75f, 0.4f) };

    /// <summary>A grey wrapped note.</summary>
    private static Label Note(string text) => new()
    {
        Text = text, Modulate = new Color(0.7f, 0.7f, 0.7f), AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(200, 0),
    };

    /// <summary>A number as text (for status lines).</summary>
    internal static string Text(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
