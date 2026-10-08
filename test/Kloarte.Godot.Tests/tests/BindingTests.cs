using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Kloarte.Tests;

/// <summary>
/// Headless tests of Godot-binding code that needs the engine (key map in Godot's InputMap, app paths and gobo textures).
/// The model and MagicQ tests are xUnit projects (test/Kloarte.Model.Tests, test/Kloarte.MagicQ.Tests). Writes only to
/// tests/output/binding. Prints "BINDING OK".
/// Run:  Godot_console.exe --headless --path . res://tests/binding_tests.tscn
/// </summary>
public partial class BindingTests : Node
{
    private int _failures;
    private string _out;

    /// <summary>Runs all tests and quits with exit code 0 when they pass.</summary>
    public override void _Ready()
    {
        _out = ProjectSettings.GlobalizePath("res://tests/output/binding");
        Directory.CreateDirectory(_out);
        foreach (var (name, test) in Tests())
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                Check(false, $"{name}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
        }
        GD.Print(_failures == 0 ? "BINDING OK" : $"BINDING FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private IEnumerable<(string, Action)> Tests()
    {
        yield return ("key map", KeyMapTest);
        yield return ("app paths", AppPathsTest);
        yield return ("paths", PathsTest);
    }

    private void Check(bool ok, string what)
    {
        if (ok)
            GD.Print($"ok   {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL {what}");
        }
    }
}
