using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The stage through an actor's eyes: the camera sits in the chosen actor's head (standing, seated or lying) and looks
/// where it faces, following it through the timeline, so you see which lamps blind it and where it stands in its light.
/// The actor is picked in the toolbar, or follows the actor selected elsewhere. Going on and off stage is announced
/// (a banner "OP cour" / "AF jardin" when the running playhead passes it) and the next one is shown under the picture.
/// Dragging with the right mouse button sideways turns the actor itself (its facing in the current cue's step,
/// recorded like the Facing slider; <see cref="FacingRequested"/>) and up / down looks up / down (the view only); the
/// wheel zooms, the middle button looks straight ahead again.
/// </summary>
public partial class ActorView : View3D
{
    /// <summary>Field of view of the eyes (degrees).</summary>
    private const float EyeFov = 70f;

    /// <summary>Seconds a banner stays.</summary>
    private const double BannerTime = 4;

    /// <summary>The mannequins' eye (model height 1.76 m): height standing, and forward of the head's centre.</summary>
    private const float EyeHeight = 1.67f, EyeForward = 0.11f;

    /// <summary>How much lower the eyes are seated (the mannequin's upper body drop).</summary>
    private const float SeatedDrop = 0.46f;

    /// <summary>The actor picker and where the actor is.</summary>
    private HBoxContainer _bar;
    private OptionButton _actors;

    /// <summary>The actor's ids in the picker's order.</summary>
    private readonly List<string> _ids = new();

    /// <summary>Over the picture: the banner (OP / AF) and the line with the actor's next entrance.</summary>
    private Label _banner, _next;

    /// <summary>Seconds the banner still shows.</summary>
    private double _bannerLeft;

    /// <summary>Looking up / down (radians); sideways the actor itself turns.</summary>
    private float _pitch;
    private bool _looking;

    /// <summary>The timeline whose entrances are cached, and those entrances.</summary>
    private PlaybackTimeline _entrancesOf;
    private List<ActorEntrance> _entrances = new();

    /// <summary>Creates the actor view with its own camera.</summary>
    public ActorView() : base("Actor", new Camera3D { Fov = EyeFov, Near = 0.03f }) { }

    /// <inheritdoc />
    public override string ShowAction => "view_actor";

    /// <inheritdoc />
    public override string Context => KeyMap.Global;

    /// <inheritdoc />
    public override bool OwnCamera => true;

    /// <summary>
    /// Raised while the right mouse button is dragged sideways: the actor (id) and the facing in degrees (-180..180, 0 =
    /// towards the audience) the drag asks for; the panel records it in the actor's step.
    /// </summary>
    public event Action<string, float> FacingRequested;

    /// <summary>The playback (its playhead and entrances); set by the panel.</summary>
    public CuePlayer Player
    {
        get => _player;
        set
        {
            if (_player != null)
                _player.EntrancePassed -= OnEntrancePassed;
            _player = value;
            if (_player != null)
                _player.EntrancePassed += OnEntrancePassed;
        }
    }

    /// <summary>Backing field of <see cref="Player"/>.</summary>
    private CuePlayer _player;

    /// <summary>The actor whose eyes the view shows (null = none).</summary>
    public string ActorId { get; private set; }

    /// <summary>The banner's text while it shows, else "" (tests).</summary>
    internal string Banner => _banner.Visible ? _banner.Text : "";

    /// <summary>The line under the picture (tests).</summary>
    internal string NextText => _next.Text;

    /// <summary>Adds the actor picker and the texts over the picture.</summary>
    public override void _Ready()
    {
        base._Ready();
        _bar = new HBoxContainer { Name = "ActorPicker" };
        Toolbar = _bar;
        _actors = new OptionButton { TooltipText = "The actor whose eyes you look through (selecting an actor elsewhere picks it too)" };
        _actors.ItemSelected += i => Follow(_ids.ElementAtOrDefault((int)i));
        _bar.AddChild(new Label { Text = "Actor" });
        _bar.AddChild(_actors);
        _bar.AddChild(new Label { Text = "  Right mouse drag: turn the actor (sideways) / look up and down, wheel: zoom, middle button: straight ahead", Modulate = new Color(1, 1, 1, 0.6f) });

        _banner = new Label
        {
            Name = "Banner",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
            LabelSettings = new LabelSettings { FontSize = 40, FontColor = new Color(1f, 0.85f, 0.3f), OutlineSize = 8, OutlineColor = Colors.Black },
        };
        _banner.SetAnchorsPreset(LayoutPreset.CenterTop);
        _banner.GrowHorizontal = GrowDirection.Both;
        _banner.Position = new Vector2(0, 24);
        AddChild(_banner);
        _next = new Label
        {
            Name = "Next",
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 15, OutlineSize = 5, OutlineColor = Colors.Black },
        };
        _next.SetAnchorsPreset(LayoutPreset.BottomLeft);
        _next.GrowVertical = GrowDirection.Begin;   // grows up from 8 px above the bottom
        _next.OffsetLeft = 10;
        _next.OffsetTop = _next.OffsetBottom = -8;
        AddChild(_next);

        Selection.Changed += OnSelectionChanged;
        TreeExiting += () =>
        {
            Selection.Changed -= OnSelectionChanged;
            Player = null;
        };
        OnShowChanged();
    }

    /// <summary>The picker only shows on the main view; the line under the picture not on a card.</summary>
    protected override void OnRoleChanged()
    {
        if (_bar != null)
            _bar.Visible = IsMain;
        if (_next != null)
            _next.Visible = !Compact;
    }

    /// <summary>Fills the picker with the show's actors; keeps the actor when it is still there.</summary>
    protected override void OnShowChanged()
    {
        if (_actors == null)
            return;
        RefreshActors();
        Follow(ActorId != null && _ids.Contains(ActorId) ? ActorId : Selection.Actor ?? _ids.FirstOrDefault());
    }

    /// <summary>The picker's items from the blocking (names may have changed).</summary>
    private void RefreshActors()
    {
        _actors.Clear();
        _ids.Clear();
        foreach (var def in Builder?.Model?.Blocking.Actors ?? new List<ActorDef>())
        {
            _ids.Add(def.Id);
            _actors.AddItem(def.Name);
        }
    }

    /// <summary>Looks through another actor's eyes (straight ahead again).</summary>
    public void Follow(string actorId)
    {
        if (_actors == null)
            return;
        if (actorId != null && !_ids.Contains(actorId))
            RefreshActors();   // added since the show was built
        ActorId = _ids.Contains(actorId) ? actorId : null;
        int index = ActorId == null ? -1 : _ids.IndexOf(ActorId);
        _actors.Select(index);
        _pitch = 0;
        _bannerLeft = 0;
        _banner.Visible = false;
    }

    /// <summary>An actor selected elsewhere (Actors list, timeline lane, 3D) is the one to look through.</summary>
    private void OnSelectionChanged()
    {
        if (Selection.Actor != null && Selection.Actor != ActorId)
            Follow(Selection.Actor);
    }

    /// <summary>The running playhead passed an actor going on / off stage: this actor's gets the banner.</summary>
    private void OnEntrancePassed(ActorEntrance e)
    {
        if (e.ActorId != ActorId)
            return;
        _banner.Text = $"{NameOf(e.ActorId)} {e.Text}";
        _banner.Visible = true;
        _bannerLeft = BannerTime;
    }

    /// <summary>The actor's name (its id when it has none).</summary>
    private string NameOf(string actorId) => Builder?.Model?.Blocking.Find(actorId)?.Name is { Length: > 0 } name ? name : actorId;

    /// <summary>Puts the camera in the actor's head, counts the banner down and says what comes next.</summary>
    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_bannerLeft > 0 && (_bannerLeft -= delta) <= 0)
            _banner.Visible = false;
        var actor = ActorId != null && Builder != null && IsInstanceValid(Builder) ? Builder.Actors.GetValueOrDefault(ActorId) : null;
        if (actor == null || !IsInstanceValid(actor))
        {
            _next.Text = _ids.Count == 0 ? "No actors (add them in the Blocking workspace)" : "";
            return;
        }
        float height = Builder.Model?.Blocking.Find(ActorId)?.EffectiveHeight ?? ActorDef.DefaultMaleHeight;
        Camera.GlobalTransform = actor.GlobalTransform * EyeTransform(actor.Pose, height / ActorDef.DefaultMaleHeight);
        if (!_next.Visible)
            return;
        _next.Text = (actor.Visible ? $"Facing {actor.Facing:0}°  ·  " : "(off stage)  ") + NextEntrance();
    }

    /// <summary>
    /// The eyes in the actor's own space (facing 0 = towards the audience, +Z) for a pose, scaled with its height, with
    /// the look-around turned in: standing / seated looking ahead, lying (head upstage) looking up, the top of the picture
    /// beyond the head.
    /// </summary>
    private Transform3D EyeTransform(string pose, float k)
    {
        Vector3 eye, ahead, up;
        if (pose == ActorPose.Lying)
        {
            // Actor's lying figure: turned -90° about X (face up, head upstage) and lifted (Actor.Create).
            eye = new Vector3(0, 0.12f + EyeForward * k, 0.1f - EyeHeight * k);
            ahead = Vector3.Up;
            up = Vector3.Forward;
        }
        else
        {
            float drop = pose == ActorPose.Seated ? SeatedDrop : 0f;
            eye = new Vector3(0, (EyeHeight - drop) * k, EyeForward * k);
            ahead = Vector3.Back;   // +Z
            up = Vector3.Up;
        }
        var basis = Basis.LookingAt(ahead, up) * new Basis(Vector3.Right, _pitch);
        return new Transform3D(basis, eye);
    }

    /// <summary>"Next: AF cour, cue 12 (in 8.5 s)" for the actor, from the playhead on; "" when there is none.</summary>
    private string NextEntrance()
    {
        var timeline = Player?.Timeline;
        if (timeline == null)
            return "";
        if (_entrancesOf != timeline)
        {
            _entrancesOf = timeline;
            _entrances = timeline.Entrances();
        }
        var next = _entrances.FirstOrDefault(e => e.ActorId == ActorId && e.Time > Player.Time);
        if (next == null)
            return "No more entrances or exits";
        double wait = next.Time - System.Math.Max(Player.Time, 0);
        return $"Next: {next.Text}, cue {next.CueNumber} (in {wait.ToString("0.0", CultureInfo.InvariantCulture)} s)";
    }

    /// <summary>Right mouse button looks around (relative to the actor's facing), the wheel zooms.</summary>
    protected override bool OnCameraInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _looking = mb.Pressed;
                Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                Camera.Fov = Mathf.Clamp(Camera.Fov + (wheel.ButtonIndex == MouseButton.WheelUp ? -3f : 3f), 10f, 100f);
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true }:
                _pitch = 0;   // straight ahead again
                Camera.Fov = EyeFov;
                return true;
            case InputEventMouseMotion mm when _looking:
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.003f, -1.4f, 1.4f);
                if (ActorId != null && Builder != null && IsInstanceValid(Builder) && Builder.Actors.GetValueOrDefault(ActorId) is { Visible: true } actor
                    && mm.Relative.X != 0f)
                    // To the right = the actor turns right (as the camera yaw did): its facing angle (about +Y) goes down.
                    FacingRequested?.Invoke(ActorId, Mathf.Wrap(actor.Facing - Mathf.RadToDeg(mm.Relative.X * 0.003f), -180f, 180f));
                return true;
        }
        return false;
    }
}
