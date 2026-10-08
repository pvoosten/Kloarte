using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Moving head test (rendered): Risquons-Tout RT2025_044 (three Robe T1 Profiles, Mode 1).
/// 1. the T1s are moving heads (GDTF mode, flux from the GDTF);
/// 2. into the 3rd cue: aim a T1 at a stage point (beam within 5 cm), full cyan, gobo slot 3, zoom 15°;
/// 3. a combined palette of that look: the cue refers to it and plays the same look;
/// 4. live Art-Net: a local sender sets a dimmer and the T1's pan -&gt; the lamps follow.
/// Screenshots movers.png, movers_live.png. Prints "MOVERS OK". Nothing is written to the show folder.
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/movers_capture.tscn
/// </summary>
public partial class MoversCapture : Node
{
    /// <summary>Show with T1 Profiles.</summary>
    [Export] public string ShowFile = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/Risquons-Tout_RT2025_044.shw";

    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private int _failures;

    /// <summary>Loads RT2025_044 and runs the steps.</summary>
    public override async void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;   // don't touch the user's view layout
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        var show = _scene.GetNode<ShowBuilder>("Show");
        show.ShowFile = ShowFile;
        show.MechanicsFile = "res://tests/output/movers.mechanics.json";
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = show;
        await Frames(20);

        var t1s = _builder.Movers.Keys.ToList();
        Check(t1s.Count == 3 && t1s.All(f => _builder.MoverOf(f).Mode.Name == "Mode 1 - CMY" && f.Lumens > 5000),
              $"{t1s.Count} moving heads, mode {_builder.MoverOf(t1s.FirstOrDefault())?.Mode.Name}, {t1s.FirstOrDefault()?.Lumens} lm");
        var f = t1s.First();
        var mh = _builder.MoverOf(f);
        foreach (var x in _builder.Fixtures)
            x.Intensity = 0f;
        f.Intensity = 1f;

        // Into the 3rd cue: aim, colour, gobo, zoom (values in physical units; the gobo by its slot's name).
        var design = CueEditing.Design(_builder);
        var stack = _builder.Document.CueStacks.Single();
        int index = System.Math.Min(2, stack.Steps.Count - 1);
        var cue = design.CueOrder[index];
        var layout = design.LayoutOf(f.HeadNumber);
        var target = _builder.ToGlobal(new Vector3(1f, 0f, -3f));
        var (pan, tilt) = mh.AimAt(target);
        var slot = new System.Collections.Generic.Dictionary<int, int>();
        GdtfDecoder.SetSlot(mh.Mode, slot, 1, "Gobo1", 3);
        GdtfDecoder.OpenShutter(mh.Mode, slot, 1);
        CueEditing.Edit(_builder, "test look", _ =>
        {
            DesignEdits.Set(cue.Heads, f.HeadNumber, "intensity", LookValue.Of(100));
            DesignEdits.Set(cue.Heads, f.HeadNumber, "Pan", LookValue.Of(System.Math.Round(pan, 3)));
            DesignEdits.Set(cue.Heads, f.HeadNumber, "Tilt", LookValue.Of(System.Math.Round(tilt, 3)));
            DesignEdits.Set(cue.Heads, f.HeadNumber, "ColorSub_C", LookValue.Of(1));
            DesignEdits.Set(cue.Heads, f.HeadNumber, "Zoom", LookValue.Of(15));
            foreach (string key in new[] { "Gobo1", "Shutter1" })
                if (layout[key] is { } channel && channel.Read(slot, 1) is { } dmx)
                    DesignEdits.Set(cue.Heads, f.HeadNumber, key, channel.FromDmx(dmx));
        });
        _builder.ApplyMovingHeads(_builder.DmxAfter(stack, index));
        await Frames(3);
        var o = f.GlobalPosition;
        var d = -f.GlobalBasis.Z.Normalized();
        var hit = o + d * ((target.Y - o.Y) / d.Y);
        Check(hit.DistanceTo(target) < 0.05f, $"aimed: pan {pan:0.0}°, tilt {tilt:0.0}°, beam hits {hit.DistanceTo(target) * 100:0.0} cm from the target");
        Check(f.GelColor.R < 0.05f && f.Gobo != null && Mathf.Abs(f.FieldAngle - 15f) < 0.3f, $"cyan ({f.GelColor.R:0.00}), gobo, zoom {f.FieldAngle:0.0}°");
        var cam = _scene.GetNode<Camera3D>("Camera");
        _panel.Visible = false;
        cam.GlobalPosition = _builder.ToGlobal(new Vector3(-2f, 2.2f, 1.5f));
        cam.LookAt(target + new Vector3(0, 0.8f, 0));
        await Frames(15);
        Shot("movers.png");

        // A combined palette of the look (position, colour, beam); the cue refers to it and plays the same.
        CueEditing.Edit(_builder, "test palette", d =>
        {
            var palette = new DesignPalette { Name = "kruimels test look" };
            foreach (string key in new[] { "Pan", "Tilt", "ColorSub_C", "Zoom" })
            {
                DesignEdits.Set(palette.Heads, f.HeadNumber, key, cue.Of(f.HeadNumber)[key]);
                DesignEdits.Set(cue.Heads, f.HeadNumber, key, LookValue.OfPalette(palette.Name));
            }
            d.Palettes.Add(palette);
        });
        int panAddress = f.DmxAddress + mh.Mode.Channel("Pan").Offsets[0] - 1;
        Check(design.ChannelsOf(cue).Any(c => c.Address == panAddress && c.Palette == "kruimels test look") && design.Problems().Count == 0,
              $"palette \"kruimels test look\" used in cue {cue.Cue} for pan, tilt, cyan and zoom");
        _builder.ApplyMovingHeads(_builder.DmxAfter(stack, index));
        Check(Mathf.Abs(mh.State.Pan - pan) < 0.5f && mh.State.R < 0.05f, $"the cue plays the palette: pan {mh.State.Pan:0.0}°");

        // Live Art-Net.
        _panel.SetLiveInput(true);
        var dimmer = _builder.Fixtures.First(x => _builder.MoverOf(x) == null && x.DmxAddress < 512);
        var channels = new byte[512];
        channels[dimmer.DmxAddress - 1] = 255;
        int uni = (f.DmxAddress - 1) / 512;
        var t1Channels = new byte[512];
        int panIndex = (panAddress - 1) % 512;
        t1Channels[panIndex] = 0;          // pan fully to one side
        t1Channels[panIndex + 1] = 0;
        t1Channels[(f.DmxAddress - 1) % 512 + mh.Mode.Channel("Dimmer").Offsets[0] - 1] = 255;
        var p0 = ArtNetReceiver.Packet(0, channels);
        var p1 = ArtNetReceiver.Packet(uni, uni == 0 ? Merge(channels, t1Channels) : t1Channels);
        using (var udp = new UdpClient())
        {
            for (int i = 0; i < 3; i++)
            {
                udp.Send(p0, p0.Length, "127.0.0.1", ArtNetReceiver.Port);
                udp.Send(p1, p1.Length, "127.0.0.1", ArtNetReceiver.Port);
                await Frames(5);
            }
        }
        if (_panel.ArtNet.Packets == 0)
        {
            // Another program on UDP 6454 (MagicQ PC, a running Kloarte) gets unicast packets instead of the test's
            // receiver (Windows hands each one to a single socket): check the rest with the same packets fed directly.
            GD.Print("note: UDP 6454 is taken by another program (MagicQ PC / Kloarte?); Art-Net packets fed to the receiver directly");
            for (int i = 0; i < 3; i++)
            {
                _panel.ArtNet.Receive(p0);
                _panel.ArtNet.Receive(p1);
                await Frames(5);
            }
        }
        await Frames(5);
        var (panMin, _) = mh.Range("Pan");
        Check(_panel.ArtNet.Packets >= 3 && Mathf.IsEqualApprox(dimmer.Intensity, 1f) && Mathf.Abs(mh.State.Pan - panMin) < 1f && f.Intensity > 0.99f,
              $"Art-Net: {_panel.ArtNet.Packets} packets, {dimmer.Name} at {dimmer.Intensity:0.00}, T1 pan {mh.State.Pan:0.0}°");
        Shot("movers_live.png");
        _panel.SetLiveInput(false);

        // The show's T1s are in Mode1; a T1 added in the plan comes in Mode 3 (user): its fixture type from the catalogue's.
        var added = _builder.AddLampInPlan("robe-t1-profile", new System.Numerics.Vector2(2f, -3f));
        var addedVis = _builder.VisOf(added);
        string addedMode = addedVis == null ? null : _builder.Document.LampPatchOf(addedVis).Personality;
        int addedChannels = addedVis == null ? 0 : _builder.Document.ChannelCountOfHead(addedVis.HeadNumber);
        Check(addedMode == "Robe_RobinT1Profile_Mode3" && addedChannels == 53 && _builder.MoverOf(added)?.Mode.Name.StartsWith("Mode 3") == true,
              $"a T1 added to a show with Mode1 T1s: {addedMode}, {addedChannels} channels, GDTF {_builder.MoverOf(added)?.Mode.Name}");

        GD.Print(_failures == 0 ? "MOVERS OK" : $"MOVERS FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private static byte[] Merge(byte[] a, byte[] b) => a.Zip(b, (x, y) => (byte)System.Math.Max(x, y)).ToArray();

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }

    private void Check(bool ok, string what)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
