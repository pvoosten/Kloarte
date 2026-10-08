using System.Runtime.CompilerServices;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Base of the converted Godot tests: <see cref="Check"/> (an assertion with the old message), the MagicQ show folder
/// and an output folder per test class. Sets the test app paths.
/// </summary>
public abstract class TestBase
{
    /// <summary>MagicQ show folder (read only).</summary>
    protected const string ShowFolder = TestEnvironment.ShowFolder;

    /// <summary>This test class's output folder.</summary>
    protected readonly string _out;

    /// <summary>Creates the output folder `outputName` under test/output.</summary>
    protected TestBase(string outputName) => _out = TestEnvironment.Output(outputName);

    /// <summary>Fails the test with `what` when `ok` is false.</summary>
    protected static void Check(bool ok, string what) => Assert.True(ok, what);

    /// <summary>A show file of the MagicQ show folder, by file name.</summary>
    protected static string Show(string name) => TestEnvironment.Show(name);

    /// <summary>App paths without Godot for every test of the assembly.</summary>
    [ModuleInitializer]
    internal static void SetAppPaths() => AppPaths.Current = new TestAppPaths();
}
