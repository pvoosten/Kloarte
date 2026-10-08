using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;
using Xunit.Abstractions;

namespace Kloarte.Tests;

/// <summary>
/// The import of MagicQ shows as native shows (<see cref="MagicQImport"/>), against every show of the MagicQ show folder
/// (read only): after every cue of the main stack the native show gives MagicQ's DMX.
/// </summary>
public sealed class DesignImportTests : TestBase
{
    /// <summary>Test output.</summary>
    private readonly ITestOutputHelper _log;

    /// <summary>Output to test/output/design_import.</summary>
    public DesignImportTests(ITestOutputHelper log) : base("design_import") => _log = log;

    /// <summary>The fixture catalogue.</summary>
    private static FixtureCatalog Catalog => FixtureCatalog.Load(TestEnvironment.File(FixtureCatalog.DefaultPath));

    /// <summary>
    /// Every show imports exactly: the DMX after every step of the main stack (tracking, palettes resolved) is MagicQ's;
    /// the native show reads back from its file the same; nothing in it means nothing.
    /// </summary>
    [Fact]
    public void EveryShowImportsExactly()
    {
        var catalog = Catalog;
        var failures = new List<string>();
        int shows = 0, steps = 0, physical = 0, exact = 0, references = 0;
        var notes = new Dictionary<string, int>();
        foreach (string shw in Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories))
        {
            MagicQShow source;
            try
            {
                source = MagicQShow.Load(shw);
            }
            catch (Exception)
            {
                continue;   // the round trip test reports unreadable shows
            }
            shows++;
            var import = MagicQImport.Import(source, shw, catalog);
            var design = import.Show;
            foreach (string note in import.Notes)
            {
                string kind = note.Split(':')[0];
                notes[kind] = notes.GetValueOrDefault(kind) + 1;
            }
            var stack = source.CueStacks.OrderByDescending(s => s.Steps.Count).ThenBy(s => s.Number).FirstOrDefault();
            if (stack == null)
                continue;
            IShowDocument native = design;
            var designStack = native.CueStacks.Single();
            var sourceSteps = stack.Steps.GroupBy(s => s.CueId).Select(g => g.First()).ToList();
            if (designStack.Steps.Count != sourceSteps.Count)
            {
                failures.Add($"{Path.GetFileName(shw)}: {designStack.Steps.Count} cues, MagicQ {sourceSteps.Count}");
                continue;
            }
            for (int i = 0; i < sourceSteps.Count; i++)
            {
                steps++;
                var expected = TrackedLevels(source, sourceSteps, i);
                var actual = CueTracking.LevelsAfter(design, designStack, i);
                var wrong = expected.Keys.Union(actual.Keys)
                    .Where(a => expected.GetValueOrDefault(a, -1) != actual.GetValueOrDefault(a, -1)).ToList();
                if (wrong.Count > 0)
                {
                    failures.Add($"{Path.GetFileName(shw)} cue {sourceSteps[i].CueNumberText}: {wrong.Count} channels differ, e.g. " +
                                 string.Join(", ", wrong.Take(4).Select(a => $"{a}: {expected.GetValueOrDefault(a, -1)} -> {actual.GetValueOrDefault(a, -1)}")));
                    break;
                }
            }
            foreach (var value in design.CueOrder.SelectMany(c => c.Heads.Values).SelectMany(h => h.Values))
            {
                if (value.IsPalette)
                    references++;
                else if (value.Dmx != null)
                    exact++;
                else
                    physical++;
            }
            if (design.Problems().Count > 0)
                failures.Add($"{Path.GetFileName(shw)}: {design.Problems()[0]}");
        }
        _log.WriteLine($"{shows} shows, {steps} cues compared; values: {physical} readable, {exact} with an exact DMX value, {references} palette references");
        foreach (var (kind, count) in notes.OrderByDescending(n => n.Value))
            _log.WriteLine($"  note x{count}: {kind}");
        Check(shows > 0, "the MagicQ show folder has shows");
        Check(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    /// <summary>MagicQ's tracked levels after step `upTo` of these steps (each cue id once, as the import plays them).</summary>
    private static Dictionary<int, int> TrackedLevels(MagicQShow show, IReadOnlyList<MagicQShow.CueStack.Step> steps, int upTo)
    {
        var cues = new Dictionary<int, MagicQShow.Cue>();
        foreach (var cue in show.Cues)
            cues.TryAdd(cue.Id, cue);
        var state = new Dictionary<int, int>();
        for (int i = 0; i <= upTo; i++)
            if (cues.TryGetValue(steps[i].CueId, out var cue))
                foreach (var (address, value) in cue.Channels)
                    state[address] = show.Resolve(address, value);
        return state;
    }

    /// <summary>
    /// data/fixture_types.yaml (the fixture types new lamps get in a native show) holds the personalities of the console
    /// templates (data/magicq_templates.shw) with their GDTF, so an export finds the same personality. With the
    /// environment variable KLOARTE_UPDATE_SCHEMAS=1 the test writes it instead.
    /// </summary>
    [Fact]
    public void FixtureTypesAreTheTemplates()
    {
        var templates = MagicQImport.Import(TestEnvironment.File(MagicQShowEditor.TemplatesPath), Catalog).Show;
        var rig = new DesignRig { Types = templates.Rig.Types };
        string text = FileCodec.Current.Write(rig, null, new[]
        {
            FileText.HeaderLine("the fixture types of the catalogue's lamps (made from data/magicq_templates.shw by DesignImportTests)"),
        });
        string path = TestEnvironment.File(DesignTypes.TypesPath);
        if (Environment.GetEnvironmentVariable("KLOARTE_UPDATE_SCHEMAS") == "1")
            File.WriteAllText(path, text);
        Assert.Equal(text, File.ReadAllText(path));
        Assert.Contains(templates.Rig.Types, t => t.Name == DesignTypes.GenericName && t.Classes == "I");
        Assert.All(templates.Rig.Types.Where(t => t.Name != DesignTypes.GenericName), t => Assert.NotNull(t.GdtfMode));
    }

    /// <summary>An imported show written as one file reads back the same.</summary>
    [Fact]
    public void ImportedShowReadsBack()
    {
        var import = MagicQImport.Import(Show("Risquons-Tout_RT2025_044.shw"), Catalog);
        string path = Path.Combine(_out, "rt044.kshow.yaml");
        import.Show.Save(path);
        var back = DesignShow.Load(path);
        Assert.Equal(import.Show.ToText(), back.ToText());
        foreach (string note in import.Notes)
            _log.WriteLine(note);
        var t1 = import.Show.Rig.Types.First(t => t.Name.Contains("T1"));
        Assert.Equal("Mode 1 - CMY", t1.GdtfMode);
        Assert.Contains(import.Show.Palettes, p => p.Heads.Values.Any(h => h.ContainsKey("Pan")));
    }
}
