using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Truss height test (rendered): Risquons-Tout with the truss-height view as the main view.
/// LT 11 to 7.5 m: its lamps +1.5 m, every lamp's beam axis still through its focus point (&lt; 1 cm); asking 9 m is
/// clamped to the light-bar limit 7.5 m; T 22 (the horizon floods aimed at the cyc) +1 m: their focus points on the cyc
/// stay; save + reload keeps the heights. Screenshots heights.png / heights_after.png. Prints "HEIGHTS OK".
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/heights_capture.tscn
/// </summary>
public partial class HeightsCapture : Node
{
    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private TrussHeightView _view;
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
            _view = _panel.Host.Find<TrussHeightView>();
            _panel.Host.SetWorkspace(Workspace.Rig);
            _panel.Host.SetMain(_view);
            _panel.SetSidePanels(false);
        }
        if (_frame == 40)
        {
            Shot("heights.png");
            Fly("LT 11", 7.5f, 7.5f);
            Fly("LT 11", 9f, 7.5f);
            Fly("T 22", 7f, 7f);
        }
        if (_frame == 60)
        {
            Shot("heights_after.png");
            string path = ProjectSettings.GlobalizePath("res://tests/output/roundtrip/heights.shw");
            foreach (string file in new[] { path, System.IO.Path.ChangeExtension(path, ".xhw") })
                if (System.IO.File.Exists(file))
                    System.IO.File.Delete(file);   // an export never writes over a show
            GD.Print(_builder.SaveShowAs(path));
            var rig = new Rig(MagicQShow.Load(path));
            Check(Mathf.Abs(rig.Trusses.First(t => t.Name == "LT 11").Height - 7.5f) < 0.01f
                  && Mathf.Abs(rig.Trusses.First(t => t.Name == "T 22").Height - 7f) < 0.01f, "heights survive save + reload");
            GD.Print(_failures == 0 ? "HEIGHTS OK" : $"HEIGHTS FAILURES: {_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Fly(string truss, float ask, float expected)
    {
        var rig = _builder.Rig;
        var t = rig.Trusses.First(x => x.Name == truss);
        var lamps = rig.LampsOn(t.Id).Select(v => _builder.FixtureOf(v.Id)).Where(f => f != null).ToList();
        var before = lamps.ToDictionary(f => f, f => (f.Position, f.FocusPoint));
        var (min, max) = _view.Limits(t);
        _view.SetHeight(t.Id, ask, min, max);
        float h = _builder.Rig.Find(t.Id).Height;
        Check(Mathf.Abs(h - expected) < 0.01f, $"{truss}: asked {ask} m -> {h:0.00} m");
        float dy = lamps.Count == 0 ? 0 : lamps[0].Position.Y - before[lamps[0]].Position.Y;
        Check(lamps.All(f => Mathf.Abs(f.Position.Y - before[f].Position.Y - dy) < 0.01f && f.FocusPoint.IsEqualApprox(before[f].FocusPoint)),
              $"{truss}: {lamps.Count} lamps moved {dy:+0.00;-0.00} m together, focus points unchanged");
        float worst = lamps.Select(f => MissDistance(f)).DefaultIfEmpty(0).Max();
        Check(worst < 0.01f, $"{truss}: beam axes through the focus points (worst {worst * 100:0.0} cm)");
    }

    /// <summary>Distance between the lamp's beam axis and its focus point.</summary>
    private static float MissDistance(ProfileFixture f)
    {
        var o = f.GlobalPosition;
        var d = -f.GlobalBasis.Z.Normalized();
        var toFocus = f.FocusPoint - o;
        return (toFocus - d * toFocus.Dot(d)).Length();
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
