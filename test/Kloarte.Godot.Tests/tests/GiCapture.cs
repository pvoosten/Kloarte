using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// Compares indirect-light options on the show (none / SDFGI / SDFGI + stray light): screenshot + fps per mode
/// (tests/output/gi_*.png); "GI CAPTURE OK" when all are taken.
/// Run:  Godot_console.exe --path . res://tests/gi_capture.tscn
/// </summary>
public partial class GiCapture : Node
{
    private static readonly string[] Modes = { "none", "stray", "sdfgi_stray" };
    private const int FramesPerShot = 150;
    private Environment _env;
    private StrayLight _stray;
    private int _frame, _mode;
    private double _fpsSum;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        var scene = TestShow.Load("gi");   // a copy of the show: the scene opens none by itself
        AddChild(scene);
        scene.GetNode<CanvasLayer>("FixturePanel").Visible = false;
        _env = scene.GetNode<WorldEnvironment>("WorldEnvironment").Environment;
        _stray = scene.GetNode<StrayLight>("StrayLight");
        Apply();
    }

    private void Apply()
    {
        string m = Modes[_mode];
        _env.SsilEnabled = false;
        _env.SdfgiEnabled = m == "sdfgi_stray";
        _stray.Amount = m == "none" ? 0f : 0.5f;
        _fpsSum = 0;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame > FramesPerShot - 60)
            _fpsSum += Engine.GetFramesPerSecond();
        if (_frame < FramesPerShot)
            return;
        string path = ProjectSettings.GlobalizePath($"res://tests/output/gi_{Modes[_mode]}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"gi {Modes[_mode]}: {_fpsSum / 60:0} fps -> {path}");
        _frame = 0;
        if (++_mode >= Modes.Length)
        {
            GD.Print("GI CAPTURE OK");
            GetTree().Quit();
            return;
        }
        Apply();
    }
}
