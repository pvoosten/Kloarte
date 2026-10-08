using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// The GDTF files of <see cref="GdtfFixture.DefaultFolder"/>, loaded once per file name prefix (a missing file is
/// remembered as missing). Thread-safe.
/// </summary>
public static class GdtfLibrary
{
    /// <summary>The fixture types loaded so far, by prefix (null = no such file).</summary>
    private static readonly Dictionary<string, GdtfFixture> Loaded = new();

    /// <summary>The fixture type of a GDTF file name prefix ("Robe_Lighting@Robin_T1_Profile"), or null.</summary>
    public static GdtfFixture Get(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return null;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(prefix, out var known))
                return known;
            string file = GdtfFixture.Find(AppPaths.File(GdtfFixture.DefaultFolder), prefix);
            var fixture = file == null ? null : GdtfFixture.Load(file);
            Loaded[prefix] = fixture;
            return fixture;
        }
    }

    /// <summary>The mode of a fixture type by name, or null.</summary>
    public static GdtfMode Mode(GdtfFixture fixture, string mode)
    {
        if (fixture == null || string.IsNullOrEmpty(mode))
            return null;
        foreach (var m in fixture.Modes)
            if (m.Name == mode)
                return m;
        return null;
    }
}
