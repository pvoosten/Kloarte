using System.IO;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>Numbered show files and the project a show file belongs to (<see cref="ShowVersions"/>).</summary>
public sealed class ShowVersionsTests
{
    /// <summary>An empty folder of its own under test/output.</summary>
    private static string Folder(string name)
    {
        string dir = TestEnvironment.Output(Path.Combine("show_versions", name));
        foreach (string file in Directory.GetFiles(dir))
            File.Delete(file);
        return dir;
    }

    /// <summary>MagicQ's three-digit number is the version; other trailing numbers are part of the name.</summary>
    [Fact]
    public void SplitsTheVersionNumber()
    {
        Assert.Equal(("Kruimels", 4), ShowVersions.Split("Kruimels_004"));
        Assert.Equal(("Kruimels", 1), ShowVersions.Split("Kruimels"));
        Assert.Equal(("Kruimels_2026", 1), ShowVersions.Split("Kruimels_2026"));
        Assert.Equal(("RT2025", 31), ShowVersions.Split("RT2025_031"));
    }

    /// <summary>The prefixes the old Kloarte subfolder caused are not part of the base name.</summary>
    [Fact]
    public void BaseNameDropsOldKloartePrefixes()
    {
        Assert.Equal("Kruimels", ShowVersions.BaseOf(@"C:\show\Kloarte_Kloarte_Kruimels_006.shw"));
        Assert.Equal("Kruimels", ShowVersions.BaseOf("Kloarte_Kruimels_002.shw"));
        Assert.Equal("Kloarte", ShowVersions.BaseOf("Kloarte.shw"));
    }

    /// <summary>
    /// A save of the project's show in MagicQ is a version of it: numbered, spaces as underscores, or prefixed with the
    /// folder of the show MagicQ was started with.
    /// </summary>
    [Fact]
    public void RecognisesTheConsolesSaves()
    {
        Assert.True(ShowVersions.IsVersionOf("Kruimels_007.shw", "Kruimels"));
        Assert.True(ShowVersions.IsVersionOf("la_comedie_001.shw", "la comedie"));
        Assert.True(ShowVersions.IsVersionOf("Risquons-Tout_RT2025_032.shw", "RT2025", "Risquons-Tout"));
        Assert.False(ShowVersions.IsVersionOf("Risquons-Tout_RT2025_032.shw", "RT2025"));
        Assert.False(ShowVersions.IsVersionOf("Kruimelsx_002.shw", "Kruimels"));
    }

    /// <summary>
    /// The next save takes the number after the highest one (folder and current file), never overwrites, and is named
    /// as MagicQ would (spaces as underscores).
    /// </summary>
    [Fact]
    public void NextFileIsTheNextFreeNumber()
    {
        string dir = Folder("next");
        Assert.Equal(Path.Combine(dir, "Kruimels.shw"), ShowVersions.NextFile(dir, "Kruimels", null));
        File.WriteAllText(Path.Combine(dir, "Kruimels.shw"), "");
        Assert.Equal(Path.Combine(dir, "Kruimels_002.shw"), ShowVersions.NextFile(dir, "Kruimels", null));
        File.WriteAllText(Path.Combine(dir, "Kruimels_005.shw"), "");
        Assert.Equal(Path.Combine(dir, "Kruimels_006.shw"), ShowVersions.NextFile(dir, "Kruimels", null));
        Assert.Equal(Path.Combine(dir, "Kruimels_010.shw"),
            ShowVersions.NextFile(dir, "Kruimels", @"C:\elsewhere\Kruimels_009.shw"));
        Assert.Equal(Path.Combine(dir, "la_comedie.shw"), ShowVersions.NextFile(dir, "la comedie", null));
        File.WriteAllText(Path.Combine(dir, "la comedie.shw"), "");
        Assert.Equal(Path.Combine(dir, "la_comedie_002.shw"), ShowVersions.NextFile(dir, "la comedie", null));
    }
}
