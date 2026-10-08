using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// ShowBuilder: moving heads. A lamp of a moving-head model gets a <see cref="MovingHead"/> (GDTF fixture type and the
/// mode matching its MagicQ personality: same channel count, CMY / RGB by channel names). Their DMX values come from the
/// cues (palettes resolved) or live Art-Net; they are decoded with the GDTF.
/// </summary>
public partial class ShowBuilder
{
    private static readonly Dictionary<string, GdtfFixture> GdtfCache = new();
    private readonly Dictionary<ProfileFixture, MovingHead> _movers = new();

    /// <summary>Cue playback (the application's; kept over show reloads).</summary>
    public IPlaybackCommands Playback => Services.Playback;

    /// <summary>The moving heads (lamp -&gt; head).</summary>
    public IReadOnlyDictionary<ProfileFixture, MovingHead> Movers => _movers;

    /// <summary>The moving head of a lamp, or null.</summary>
    public MovingHead MoverOf(ProfileFixture f) => f != null && _movers.TryGetValue(f, out var mh) ? mh : null;

    /// <summary>The node that carries a lamp's MagicVis position: the moving head's base, else the lamp itself.</summary>
    public Node3D Mount(ProfileFixture f) => MoverOf(f) is { } mh ? mh : f;

    /// <summary>Gives a lamp of a moving-head model its yoke and head (from the model's GDTF).</summary>
    private void AttachMovingHead(ProfileFixture fixture, FixtureModel model, IRigItem item, FixtureMechanics mech, LampPatch patch)
    {
        var gdtf = LoadGdtf(model.Gdtf);
        var mode = gdtf?.ModeFor(patch.ChannelCount, patch.ChannelNames);
        if (mode == null)
        {
            GD.PushWarning($"ShowBuilder: no GDTF mode for {fixture.Name} ({patch.Personality}, {patch.ChannelCount} channels)");
            return;
        }
        var mounting = fixture.Transform;
        mounting.Basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(item.RotX), Mathf.DegToRad(item.RotY), Mathf.DegToRad(item.RotZ)), EulerOrder.Yxz);
        if (mech.Mounting == "standing" && Mathf.Abs(item.RotX) < 1f && Mathf.Abs(item.RotZ) < 1f)
            mounting.Basis *= new Basis(Vector3.Right, Mathf.Pi);   // MagicVis says hanging, the rigger says standing on the floor
        var parent = fixture.GetParent();
        parent.RemoveChild(fixture);
        var mh = MovingHead.Create(fixture, gdtf, mode, mounting);
        parent.AddChild(mh);
        if (gdtf.LuminousFlux > 0)
            fixture.Lumens = gdtf.LuminousFlux;
        _movers[fixture] = mh;
        mh.Apply(GdtfDecoder.Decode(gdtf, mode, new Dictionary<int, int>(), fixture.DmxAddress));
    }

    private static GdtfFixture LoadGdtf(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return null;
        if (GdtfCache.TryGetValue(prefix, out var cached))
            return cached;
        string file = GdtfFixture.Find(AppPaths.File(GdtfFixture.DefaultFolder), prefix);
        var gdtf = file == null ? null : GdtfFixture.Load(file);
        if (gdtf == null)
            GD.PushWarning($"ShowBuilder: no GDTF file {prefix}* in {GdtfFixture.DefaultFolder}");
        return GdtfCache[prefix] = gdtf;
    }

    /// <summary>Applies DMX values (address -&gt; 0..255) to the moving heads' attributes.</summary>
    public void ApplyMovingHeads(Dictionary<int, int> dmx) => Playback.ShowMovingHeads(dmx);

    /// <inheritdoc />
    public void ShowMovingHeads(IReadOnlyDictionary<int, int> dmx)
    {
        if (_movers.Count == 0)
            return;
        var values = dmx as Dictionary<int, int> ?? new Dictionary<int, int>(dmx);
        foreach (var (f, mh) in _movers)
            if (IsInstanceValid(mh))
                mh.Apply(GdtfDecoder.Decode(mh.Gdtf, mh.Mode, values, f.DmxAddress));
    }

    /// <summary>DMX values of a head as they are now (cue / Art-Net).</summary>
    public Dictionary<int, int> CurrentDmx(ProfileFixture f) =>
        MoverOf(f) is { } mh ? Playback.CurrentDmx(f.DmxAddress, mh.Mode.ChannelCount) : new Dictionary<int, int>();
}
