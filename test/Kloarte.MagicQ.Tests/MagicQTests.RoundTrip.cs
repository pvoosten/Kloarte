using System;
using System.IO;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.RoundTrip.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>The show MagicQ PC opens: the newest of .shw / .sbk.</summary>
    [Fact]
    public void RoundTripTest()
    {
        string folder = Path.Combine(_out, "watch");
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);

        // status.dat names an autosave: the newer of .sbk and .shw is opened.
        string a = Path.Combine(folder, "A.sbk"), b = Path.Combine(folder, "A.shw");
        File.WriteAllText(a, "autosave");
        File.WriteAllText(b, "saved");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(-5));
        Check(MagicQPc.Existing(a) == b, "a .shw saved after the autosave wins");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(5));
        Check(MagicQPc.Existing(a) == a, "a newer autosave wins");
        Check(MagicQPc.Existing(Path.Combine(folder, "missing.sbk")) == null, "no file: null");
    }
}
