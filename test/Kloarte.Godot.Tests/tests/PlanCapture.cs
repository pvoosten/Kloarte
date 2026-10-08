using System.Collections.Generic;
using System.Linq;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Application;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Plan view test (rendered): Risquons-Tout with the plan as the main view.
/// 1. screenshot tests/output/plan.png (symbols, gels, legend);
/// 2. drag head 150 (Binnen T, on LT 11) onto T 1 with the mouse -&gt; it hangs on T 1 (MagicVis parent);
/// 3. add a Manfrotto 111U stand by clicking, drag a lamp onto it;
/// 4. patch box: give head 151 a lamp of head 150 (join), check the lamp's address;
/// 5. Save PDF (tests/output/plan.pdf). Prints "PLAN OK". Nothing is written to the show folder.
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/plan_capture.tscn
/// </summary>
public partial class PlanCapture : Node
{
    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private PlanView _plan;
    private int _frame, _failures;

    /// <summary>Loads the show scene.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;   // don't touch the user's view layout
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = _scene.GetNode<ShowBuilder>("Show");
    }

    /// <summary>Runs the steps.</summary>
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 20)
        {
            _plan = _panel.Host.Find<PlanView>();
            _panel.Host.SetWorkspace(Workspace.Rig);
            _panel.Host.SetMain(_plan);
            _panel.SetSidePanels(false);
        }
        if (_frame == 40)
        {
            _plan.Fit();
            Shot("plan.png");
            Drag();
        }
        if (_frame == 60)
            Stand();
        if (_frame == 70)
            AddLamps();
        if (_frame == 80)
        {
            PatchBox();
            LampMenu();
            Shot("plan_after.png");
            string pdf = ProjectSettings.GlobalizePath("res://tests/output/plan.pdf");
            GD.Print(_plan.SavePdf(pdf));
            Check(System.IO.File.Exists(pdf) && new System.IO.FileInfo(pdf).Length > 5000, "plan PDF written");
            // A copy in tests/output: tests never open the user's show files for editing (CLAUDE.md "Tests").
            string copy = ProjectSettings.GlobalizePath("res://tests/output/plan_empty/" + EmptyShow);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
            System.IO.File.Copy(System.IO.Path.Combine(MagicQPc.ShowFolder, EmptyShow), copy, true);
            _builder.OpenShow(copy);
        }
        if (_frame == 100)
            AddToEmptyShow();
        if (_frame == 110)
        {
            Shot("plan_empty_show.png");
            GD.Print(_failures == 0 ? "PLAN OK" : $"PLAN FAILURES: {_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    /// <summary>A show with only the venue (MagicVis objects): no personality, patch or lamp.</summary>
    private const string EmptyShow = "Theaterzaal_Brouckere.shw";

    /// <summary>
    /// In a show without lamps, clicking adds a C103 and a T1 Profile anyway: their personality, patch and MagicVis records
    /// come from data/magicq_templates.shw.
    /// </summary>
    private void AddToEmptyShow()
    {
        var show = (DesignShow)_builder.Model.Show;
        Check(!_builder.Fixtures.Any() && show.Rig.Types.Count == 0, $"{EmptyShow}: no lamps, no fixture types");
        _plan.Fit();
        foreach (var (model, at) in new[] { ("adb-c103", new SysVec2(-1f, -2f)), ("robe-t1-profile", new SysVec2(1f, -2f)) })
        {
            _plan.BeginLamp(model);
            var p = _plan.ToView(at);
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p });
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p });
        }
        _plan.HandleKey(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
        var lamps = _builder.Fixtures.ToList();
        Check(lamps.Count == 2 && lamps.Select(f => _builder.ModelOf(f)?.Id).Order().SequenceEqual(new[] { "adb-c103", "robe-t1-profile" }),
              $"added to the empty show by clicking: {string.Join(", ", lamps.Select(f => _builder.ModelOf(f)?.Id))}");
        var t1 = _builder.MoverOf(lamps.First(f => _builder.ModelOf(f)?.IsMoving == true));
        int patched = show.Rig.Heads.Sum(h => h.Channels);
        Check(show.Rig.Types.Count == 2 && patched == 1 + 53 && t1?.Mode.Name.StartsWith("Mode 3") == true,
              $"fixture types {show.Rig.Types.Count}, {patched} patched channels, the T1 is a moving head in GDTF {t1?.Mode.Name}");
    }

    private ProfileFixture Lamp(int address) => _builder.Fixtures.First(f => f.DmxAddress == address);

    private void Mouse(InputEvent e) => _plan._GuiInput(e);

    private void Drag()
    {
        var f = Lamp(150);
        var t1 = _builder.Rig.Trusses.First(t => t.Name == "T 1");
        var from = _plan.ToView(new SysVec2(f.Position.X, f.Position.Z));
        var to = _plan.ToView(new SysVec2(f.Position.X + 0.5f, t1.Start.Z - 9.75f + 0.03f));
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        for (int i = 1; i <= 5; i++)
            Mouse(new InputEventMouseMotion { Position = from.Lerp(to, i / 5f), ButtonMask = MouseButtonMask.Left });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
        var vis = _builder.VisOf(f);
        Check(vis.ParentId == t1.Id && Mathf.Abs(vis.Z - t1.Start.Z) < 0.01f, $"head 150 dragged onto T 1 (parent {vis.ParentId:x}, z {vis.Z:0.00})");
        Check(Selection.Fixture == f, "dragged lamp is selected");
    }

    private void Stand()
    {
        _plan.BeginStand("Manfrotto 111U");
        var at = _plan.ToView(new SysVec2(-3f, 1f));
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        var stand = _builder.Rig.Stands.FirstOrDefault(s => s.Name.StartsWith("Stand Manfrotto 111U"));
        Check(stand != null, "stand added by clicking");
        // Drag a lamp onto the stand.
        var f = Lamp(151);
        var from = _plan.ToView(new SysVec2(f.Position.X, f.Position.Z));
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        Mouse(new InputEventMouseMotion { Position = at + new Vector2(3, 2), ButtonMask = MouseButtonMask.Left });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at + new Vector2(3, 2) });
        Check(stand != null && _builder.VisOf(f).ParentId == stand.Id && Mathf.Abs(f.Position.Y - (stand.Height + Rig.StandMount)) < 0.02f,
              $"lamp of head 151 stands on the stand at {f.Position.Y:0.00} m");
    }

    /// <summary>
    /// Adds a C103 near LT 11 and a LEDBeam near T 1 by clicking (each on the nearest truss, own head and DMX), writes
    /// and re-reads the show, then removes both with Delete (their unused heads are unpatched again).
    /// </summary>
    private void AddLamps()
    {
        var show = (DesignShow)_builder.Model.Show;
        int Patched(DesignShow s) => s.Rig.Heads.Sum(h => h.Channels);
        int lampsBefore = _builder.Fixtures.Count(), patchBefore = Patched(show), maxHead = show.Rig.Heads.Max(h => h.Number);
        var lt11 = _builder.Rig.Trusses.First(t => t.Name == "LT 11");
        var t1 = _builder.Rig.Trusses.First(t => t.Name == "T 1");
        float StageZ(RigSupport t) => _builder.ToStage(t.Start.X, t.Start.Y, t.Start.Z).Z;

        void ClickAt(SysVec2 p)
        {
            var at = _plan.ToView(p);
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        }
        _plan.BeginLamp("adb-c103");
        ClickAt(new SysVec2(1.2f, StageZ(lt11) + 0.1f));
        var pc = Selection.Fixture;
        _plan.BeginLamp("robe-ledbeam150");
        ClickAt(new SysVec2(-1.3f, StageZ(t1) - 0.1f));
        var beam = Selection.Fixture;
        _plan.HandleKey(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });

        var pcVis = _builder.VisOf(pc);
        var beamVis = _builder.VisOf(beam);
        Check(pc != null && beam != null && pc != beam && _builder.Fixtures.Count() == lampsBefore + 2, "two lamps added by clicking");
        Check(pcVis?.ParentId == lt11.Id && Mathf.Abs(pc.Position.X - 1.2f) < 0.05f && _builder.ModelOf(pc)?.Id == "adb-c103",
              $"C103 hangs on LT 11 at x {pc?.Position.X:0.00} (parent {_builder.Rig.Find(pcVis?.ParentId ?? 0)?.Name}, {pcVis?.ParentId:x} vs {lt11.Id:x}, model {_builder.ModelOf(pc)?.Id})");
        Check(beamVis?.ParentId == t1.Id && _builder.ModelOf(beam)?.Id == "robe-ledbeam150", "LEDBeam hangs on T 1");
        Check(pcVis.HeadNumber == maxHead + 1 && beamVis.HeadNumber == maxHead + 2 && Patched(show) == patchBefore + 1 + 22
              && show.ChannelCountOfHead(pcVis.HeadNumber) == 1 && show.HeadAt(pcVis.Address) == pcVis.HeadNumber,
              $"own heads: {pcVis.HeadNumber} at {DmxAddress.Format(pcVis.Address)}, {beamVis.HeadNumber} at {DmxAddress.Format(beamVis.Address)} (22 ch)");

        string path = ProjectSettings.GlobalizePath("res://tests/output/plan_added.shw");
        foreach (string file in new[] { path, System.IO.Path.ChangeExtension(path, ".xhw") })
            if (System.IO.File.Exists(file))
                System.IO.File.Delete(file);   // an export never writes over a show
        _builder.SaveShowAs(path);
        var re = MagicQShow.Load(path);
        Check(re.Vis.Any(v => v.Id == pcVis.Id && v.ParentId == lt11.Id && v.HeadNumber == pcVis.HeadNumber)
              && re.Patch.Count(p => p.HeadNumber == beamVis.HeadNumber) == 22, "added lamps and their patch read back");

        Selection.Select(new[] { pc, beam });
        _builder.Checkpoint();   // adding them is one undo step, the Delete the next
        _plan.HandleKey(new InputEventKey { PhysicalKeycode = Key.Delete, Keycode = Key.Delete, Pressed = true });
        Check(_builder.Fixtures.Count(f => !f.IsQueuedForDeletion()) == lampsBefore && Patched(show) == patchBefore
              && show.Rig.Items.All(v => v.Id != pcVis.Id),
              $"Delete removes them and their unused heads (lamps {_builder.Fixtures.Count(f => !f.IsQueuedForDeletion())} vs {lampsBefore}, "
              + $"patch {Patched(show)} vs {patchBefore}, gone {show.Rig.Items.All(v => v.Id != pcVis.Id)})");

        // Undo brings them back with their patch (UI review 6; the model's show is a new document then), redo removes
        // them again.
        string undone = _builder.Undo();
        var back = (DesignShow)_builder.Model.Show;
        Check(undone == "Remove lamp" && back.Rig.Items.Any(v => v.Id == pcVis.Id) && Patched(back) == patchBefore + 1 + 22
              && _builder.Fixtures.Count(f => !f.IsQueuedForDeletion()) == lampsBefore + 2,
              $"undo '{undone}': lamps and patch back (patch {Patched(back)} vs {patchBefore + 1 + 22})");
        string redone = _builder.Redo();
        var again = (DesignShow)_builder.Model.Show;
        Check(redone == "Remove lamp" && again.Rig.Items.All(v => v.Id != pcVis.Id) && Patched(again) == patchBefore,
              $"redo '{redone}': removed again");
    }

    /// <summary>A right click on a lamp in the plan opens the lamp menu (UI review 9).</summary>
    private void LampMenu()
    {
        var f = Lamp(150);   // the patch box test just moved 151 onto head 150
        var at = _plan.ToView(new SysVec2(f.Position.X, f.Position.Z));
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
        var menu = _panel.FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>().FirstOrDefault(m => m.Visible);
        var items = menu == null ? new List<string>() : Enumerable.Range(0, menu.ItemCount).Select(menu.GetItemText).ToList();
        Check(items.Any(i => i.StartsWith("Patch / gel")) && items.Any(i => i.StartsWith("Remove")) && Selection.Fixtures.Contains(f),
              $"right click in the plan: lamp menu [{string.Join(" | ", items)}]");
        menu?.Hide();
    }

    private void PatchBox()
    {
        var f = Lamp(151);
        Selection.Select(f);
        string status = _plan.Patch(150, "1-151");
        Check(f.HeadNumber == 150 && f.DmxAddress == 150, $"patch box: {status}");
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
