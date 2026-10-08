using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the right-hand settings of the selected lamp. What is shown depends on the lamp's catalogue model
/// (fixture type): a profile has zoom, focus, shutters, iris and gobo; a PC / fresnel spot-flood and barndoors; a PAR
/// its lamp and oval rotation; an ACP its mounting. Mechanical settings (fixed for the whole show, saved in the
/// mechanics JSON / MagicQ patch) and per-cue settings (intensity; moving heads: palettes) are separate sections.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The scroll container around the settings.</summary>
    private ScrollContainer _paramScroll;

    /// <summary>What the settings were last built for (a lamp, or an actor key); a rebuild for the same thing keeps
    /// the scroll position and the focused control.</summary>
    private object _paramsSubject;

    /// <summary>Counts rebuilds, so a late restore of an older rebuild is skipped.</summary>
    private int _paramsBuild;

    /// <summary>
    /// Removes the settings before they are built again for `subject`. When it is the same lamp / actor as before, the
    /// scroll position and keyboard focus are put back once the new controls are laid out (a rebuild after a change
    /// must not throw the user out of the field they are working in).
    /// </summary>
    private void ClearParams(object subject)
    {
        bool same = subject != null && Equals(subject, _paramsSubject);
        int scroll = _paramScroll.ScrollVertical;
        var focus = same ? FocusPath() : null;
        foreach (Node child in _params.GetChildren())
        {
            _params.RemoveChild(child);
            child.QueueFree();
        }
        _paramsSubject = subject;
        int build = ++_paramsBuild;
        if (same)
            RestoreParamsView(build, scroll, focus);
        else
            _paramScroll.ScrollVertical = 0;
    }

    /// <summary>Child indices from the settings box to the focused control, or null when the focus is elsewhere.</summary>
    private List<int> FocusPath()
    {
        var focused = GetViewport().GuiGetFocusOwner();
        if (focused == null || !_params.IsAncestorOf(focused))
            return null;
        var path = new List<int>();
        for (Node n = focused; n != _params; n = n.GetParent())
            path.Insert(0, n.GetIndex(true));
        return path;
    }

    /// <summary>After the layout of a rebuild: scroll back and focus the control at the same place.</summary>
    private async void RestoreParamsView(int build, int scroll, List<int> focus)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (build != _paramsBuild || !IsInstanceValid(_paramScroll))
            return;
        _paramScroll.ScrollVertical = scroll;
        if (focus == null)
            return;
        Node n = _params;
        foreach (int i in focus)
        {
            if (i >= n.GetChildCount(true))
                return;
            n = n.GetChild(i, true);
        }
        if (n is Control { FocusMode: not Control.FocusModeEnum.None } c && c.IsVisibleInTree())
            c.GrabFocus();
    }

    /// <summary>Rebuilds the settings of the selected lamp.</summary>
    private void BuildParams()
    {
        _lampParamsCue = null;   // set again when the lamp's programmer buttons are built
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        if (lamps.Count > 1)
        {
            BuildMultiParams(lamps);
            return;
        }
        var f = _selected;
        ClearParams(f);
        if (f == null || !IsInstanceValid(f))
            return;
        var builder = BuilderOf(f);
        var model = builder?.ModelOf(f);

        _params.AddChild(new Label { Text = LampTitle(f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (f.DmxAddress > 0)
            _params.AddChild(new Label
            {
                Text = $"Head {f.HeadNumber}, DMX {DmxAddress.Format(f.DmxAddress)}",
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });
        if (builder != null && f.HeadNumber > 0)
        {
            int onHead = builder.Fixtures.Count(l => l.HeadNumber == f.HeadNumber && IsInstanceValid(l));
            if (onHead > 1)
                _params.AddChild(new Label
                {
                    Text = $"{onHead} lamps on head {f.HeadNumber}: gel and intensity are per head",
                    Modulate = new Color(0.7f, 0.7f, 0.7f),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    CustomMinimumSize = new Vector2(200, 0),
                });
            GelField(_params, new List<ProfileFixture> { f }, builder, () => CallDeferred(MethodName.BuildParams));
        }

        if (builder != null)
        {
            TypePicker(f, builder, model);
            Rigging(f, builder);
        }

        Header(_player.Stack != null && _player.Step >= 0 ? $"Cue {_player.Stack.Steps[_player.Step].CueNumberText}" : "This cue",
            "The values of this lamp's head in the current cue: white = set in this cue, blue = from a palette, grey = tracked from a cue before");
        if (builder == null)
            IntensitySlider(() => f.Intensity, v => f.Intensity = v);
        else
            CueValues(f, builder);

        Header("Mechanics (whole show)");
        Slider("Output %", 0, 150, 1, () => f.Output * 100f, v => f.Output = v / 100f,
            "Light output of this lamp (100 % = the catalogue's lumens; an old lamp or a dirty lens gives less)");
        Optics(f, builder, model);
        if (model == null || model.Focus)
            Slider("Focus", 0, 0.3, 0.001, () => f.Softness, v => f.Softness = v, "Edge softness: 0 = sharp, more = softer");
        if (model == null || model.Shutters)
            ShutterSettings(f);
        if (model == null || model.Iris)
            Slider("Iris", 0, 1, 0.01, () => f.Iris, v => f.Iris = v, "0 = open, 1 = closed");
        if (model == null || model.GoboSize != null)
        {
            GoboPicker(f);
            Slider("Gobo °", -180, 180, 1, () => f.GoboRotation, v => f.GoboRotation = v, "Rotation of the gobo in its holder (°)");
            Slider("Gobo size", 0.2, 2, 0.01, () => f.GoboSize, v => f.GoboSize = v, "1 = the gobo fills the gate");
        }
        if (model is { Barndoors: true })
            BarndoorSettings(f);
        if (model is { Asymmetric: true } && builder != null)
            Mounting(f, builder, new[] { "top", "bottom" }, "Hung above the cloth (top) or ground row (bottom)");
        if (model is { IsMoving: true } && builder != null)
            Mounting(f, builder, new[] { "hanging", "standing" }, "Moving head hung from a truss or standing on the floor");

        if (f.AutoAim)
        {
            Header("Focus point (m)");
            Slider("X", -12, 12, 0.05, () => f.FocusPoint.X, v => f.FocusPoint = f.FocusPoint with { X = v });
            Slider("Y", -1, 8, 0.05, () => f.FocusPoint.Y, v => f.FocusPoint = f.FocusPoint with { Y = v });
            Slider("Z", -15, 15, 0.05, () => f.FocusPoint.Z, v => f.FocusPoint = f.FocusPoint with { Z = v });
        }
    }

    /// <summary>Fixture type (catalogue model), with "estimated" marked.</summary>
    private void TypePicker(ProfileFixture f, ShowBuilder builder, FixtureModel current)
    {
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Fixture type (catalogue model)" };
        var models = builder.Catalog?.Models ?? new();
        for (int i = 0; i < models.Count; i++)
        {
            picker.AddItem(models[i].Name + (models[i].Estimated ? "  (est.)" : ""));
            if (models[i] == current)
                picker.Select(i);
        }
        if (current == null)
        {
            picker.AddItem("(unknown type)");
            picker.Select(picker.ItemCount - 1);
        }
        picker.ItemSelected += i =>
        {
            if (i < models.Count)
            {
                builder.SetModel(f, models[(int)i].Id);
                CallDeferred(MethodName.BuildParams);
                FillList();
            }
        };
        Row("Type", picker);
    }

    /// <summary>Zoom / spot-flood within the model's range (the MagicQ patch beam angle when it has one), or the PAR lamp.</summary>
    private void Optics(ProfileFixture f, ShowBuilder builder, FixtureModel model)
    {
        if (model is { Kind: FixtureKind.Par, Lamps: not null } && builder != null)
        {
            var lamps = new OptionButton();
            string current = f.HasMeta("lamp") ? f.GetMeta("lamp").AsString() : model.DefaultLamp;
            for (int i = 0; i < model.Lamps.Count; i++)
            {
                lamps.AddItem($"{model.Lamps[i].Name}  {model.Lamps[i].Beam[0]:0}x{model.Lamps[i].Beam[1]:0}°");
                if (model.Lamps[i].Id == current)
                    lamps.Select(i);
            }
            lamps.ItemSelected += i =>
            {
                builder.UpdateMechanics(f, m => m.Lamp = model.Lamps[(int)i].Id);
                CallDeferred(MethodName.BuildParams);
            };
            Row("Lamp", lamps);
            Slider("Oval °", -90, 90, 1, () => f.OvalRotation, v => f.OvalRotation = v, "Rotation of the PAR lamp's oval beam (°)");
            return;
        }
        if (model is { Asymmetric: true })
            return;
        float min = model?.HasZoom == true ? model.Zoom[0] : 1f;
        float max = model?.HasZoom == true ? model.Zoom[1] : f.MaxAngle;
        string what = model?.Kind is FixtureKind.Pc or FixtureKind.Fresnel ? "Spot-flood: field angle (°)" : "Zoom: field angle (°)";
        if (builder != null && f.DmxAddress > 0 && (model == null || model.HasZoom))
            Slider("Beam °", min, max, 1, () => f.FieldAngle, v => builder.SetBeamAngle(f, v),
                what + ", stored as the beam angle in the MagicQ patch" +
                (f.HasMeta("beamFromMagicQ") ? " (set there now)" : ""));
        else if (model == null || model.HasZoom)
            Slider("Beam °", min, max, 0.5, () => f.FieldAngle, v => f.FieldAngle = v, what);
        else
            _params.AddChild(new Label { Text = $"Fixed beam {f.FieldAngle:0}°", Modulate = new Color(0.7f, 0.7f, 0.7f) });
    }

    private void ShutterSettings(ProfileFixture f)
    {
        Header("Shutters", "Depth 0 = open, 1 = to the centre, 2 = across; angle in °");
        Slider("Top", 0, 2, 0.01, () => f.TopDepth, v => f.TopDepth = v);
        Slider("Top angle", -45, 45, 0.5, () => f.TopAngle, v => f.TopAngle = v);
        Slider("Right", 0, 2, 0.01, () => f.RightDepth, v => f.RightDepth = v);
        Slider("Right angle", -45, 45, 0.5, () => f.RightAngle, v => f.RightAngle = v);
        Slider("Bottom", 0, 2, 0.01, () => f.BottomDepth, v => f.BottomDepth = v);
        Slider("Bottom angle", -45, 45, 0.5, () => f.BottomAngle, v => f.BottomAngle = v);
        Slider("Left", 0, 2, 0.01, () => f.LeftDepth, v => f.LeftDepth = v);
        Slider("Left angle", -45, 45, 0.5, () => f.LeftAngle, v => f.LeftAngle = v);
        Slider("Rotation", -45, 45, 0.5, () => f.ShutterRotation, v => f.ShutterRotation = v);
    }

    private void BarndoorSettings(ProfileFixture f)
    {
        Header("Barndoors", "Each flap 0 = open, 1 = closed; rotation in °");
        Slider("Top flap", 0, 1, 0.01, () => f.BarndoorTop, v => f.BarndoorTop = v);
        Slider("Right flap", 0, 1, 0.01, () => f.BarndoorRight, v => f.BarndoorRight = v);
        Slider("Bottom flap", 0, 1, 0.01, () => f.BarndoorBottom, v => f.BarndoorBottom = v);
        Slider("Left flap", 0, 1, 0.01, () => f.BarndoorLeft, v => f.BarndoorLeft = v);
        Slider("Rotation", -90, 90, 0.5, () => f.BarndoorRotation, v => f.BarndoorRotation = v);
    }

    private void Mounting(ProfileFixture f, ShowBuilder builder, string[] options, string tooltip)
    {
        var picker = new OptionButton { TooltipText = tooltip };
        string current = f.HasMeta("mounting") ? f.GetMeta("mounting").AsString() : options[0];
        foreach (string o in options)
            picker.AddItem(o);
        picker.Select(Math.Max(0, Array.IndexOf(options, current)));
        picker.ItemSelected += i => builder.UpdateMechanics(f, m => m.Mounting = options[i]);
        Row("Mounting", picker);
    }

    /// <summary>
    /// Object the head hangs on (MagicVis parent) and its position in stage coordinates. Choosing a truss moves the head
    /// onto it; the head stays focused on its focus point. A head on a truss slides along it (the position fields of the
    /// directions it can't move in are disabled, with the reason in their tooltip), a head on a stand only changes
    /// height, a head without a support moves freely (<see cref="LampMotion"/>).
    /// </summary>
    private void Rigging(ProfileFixture f, ShowBuilder builder)
    {
        Header("Rigging");

        var objects = builder.RigObjects.ToList();
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        picker.AddItem("(not attached)");
        picker.SetItemMetadata(0, 0);
        int parent = builder.GetParent(f);
        foreach (var (id, name) in objects)
        {
            picker.AddItem(name);
            picker.SetItemMetadata(picker.ItemCount - 1, id);
            if (id == parent)
                picker.Select(picker.ItemCount - 1);
        }
        picker.ItemSelected += i =>
        {
            builder.SetParent(f, picker.GetItemMetadata((int)i).AsInt32());
            _marker.GlobalPosition = f.GlobalPosition;
            CallDeferred(MethodName.BuildParams);   // position fields show the snapped position
        };
        Row("Hangs on", picker);

        var motion = builder.MotionOf(f);
        var spins = new Dictionary<MotionAxis, SpinBox>();
        foreach (var (label, axis) in new[] { ("X (right +)", MotionAxis.X), ("Y (height)", MotionAxis.Y), ("Z (audience +)", MotionAxis.Z) })
        {
            var spin = Number(label, () => Coordinate(builder.Mount(f).Position, axis), v =>
            {
                string message = builder.MoveFixtureAxis(f, axis, v);
                _marker.GlobalPosition = f.GlobalPosition;
                foreach (var (other, field) in spins)   // a truss along a diagonal moves X and Z together; a stop shows
                    field.SetValueNoSignal(Coordinate(builder.Mount(f).Position, other));
                if (message != null)
                    _status.Text = message;
            });
            spins[axis] = spin;
            if (motion == null || motion.IsFree(axis))
                continue;
            spin.Editable = false;
            spin.Modulate = new Color(1, 1, 1, 0.6f);
            string why = motion.WhyFixed(axis);
            spin.TooltipText = why;
            var title = spin.GetParent().GetChild<Label>(0);
            title.TooltipText = why;
            title.MouseFilter = Control.MouseFilterEnum.Pass;   // a label ignores the mouse, and so its tooltip
        }
    }

    /// <summary>One coordinate of a position.</summary>
    private static float Coordinate(Vector3 position, MotionAxis axis) => axis switch
    {
        MotionAxis.X => position.X,
        MotionAxis.Y => position.Y,
        _ => position.Z,
    };

    /// <summary>A section header of the inspector, with an explanation in its tooltip.</summary>
    private void Header(string text, string tooltip = "")
    {
        _params.AddChild(new HSeparator());
        _params.AddChild(new Label
        {
            Text = text,
            Modulate = new Color(1f, 0.75f, 0.4f),
            TooltipText = tooltip,
            MouseFilter = tooltip.Length > 0 ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
        });
    }

    private SpinBox Number(string label, Func<float> get, Action<float> set)
    {
        var spin = new SpinBox
        {
            MinValue = -50,
            MaxValue = 50,
            Step = 0.01,
            Value = get(),
            Suffix = "m",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        spin.ValueChanged += v => set((float)v);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(spin);
        _params.AddChild(row);
        return spin;
    }

    /// <summary>
    /// A labelled slider with its value (short label, the explanation in the tooltip, UI review 19); numbers with a
    /// point, not the locale's comma.
    /// </summary>
    private void Slider(string label, double min, double max, double step, Func<float> get, Action<float> set,
                        string tooltip = "")
    {
        var (row, _) = SliderRow(label, tooltip, min, max, step, get(), v => v.ToString("0.###", CultureInfo.InvariantCulture),
            null, v => set((float)v));
        _params.AddChild(row);
    }

    /// <summary>
    /// The intensity in % (UI review 20: as the fixture list and MagicQ show it), the DMX value in the value's tooltip.
    /// `get` / `set` are 0..1.
    /// </summary>
    private void IntensitySlider(Func<float> get, Action<float> set) =>
        _params.AddChild(SliderRow("Intensity",
            "The level of these heads in the current cue (later cues track it)",
            0, 100, 1, get() * 100f, v => $"{v:0} %", v => $"DMX {Mathf.RoundToInt(v * 2.55):0}", v => set((float)(v / 100.0))).Row);

    /// <summary>A row: label, slider and the value as text (`detail` = the value's tooltip, or null).</summary>
    private static (HBoxContainer Row, HSlider Slider) SliderRow(string label, string tooltip, double min, double max,
        double step, double initial, Func<double, string> format, Func<double, string> detail, Action<double> changed)
    {
        var row = new HBoxContainer();
        var value = new Label
        {
            CustomMinimumSize = new Vector2(52, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = detail != null ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
        };
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = initial,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = tooltip,
        };
        void Show(double v)
        {
            value.Text = format(v);
            value.TooltipText = detail?.Invoke(v) ?? "";
        }
        Show(slider.Value);
        slider.ValueChanged += v =>
        {
            changed(v);
            Show(v);
        };
        row.AddChild(new Label
        {
            Text = label,
            CustomMinimumSize = new Vector2(110, 0),
            TooltipText = tooltip,
            MouseFilter = tooltip.Length > 0 ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore,
        });
        row.AddChild(slider);
        row.AddChild(value);
        return (row, slider);
    }

    private void GoboPicker(ProfileFixture f)
    {
        var paths = GoboLibrary.Images();
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        picker.AddItem("(no gobo)");
        foreach (var p in paths)
            picker.AddItem(p.GetFile().GetBaseName());
        int current = f.Gobo == null ? 0 : paths.IndexOf(f.Gobo.ResourcePath) + 1;
        picker.Selected = Math.Max(current, 0);
        picker.ItemSelected += i => f.Gobo = i == 0 ? null : GoboLibrary.Load(paths[(int)i - 1]);
        Row("Gobo", picker);
    }
}
