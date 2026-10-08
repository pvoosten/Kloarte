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
/// The export of native shows to MagicQ shows (<see cref="MagicQExport"/>): written into a copy of the source show,
/// unchanged records byte-identical, the changes read back the same (read only on the MagicQ show folder; output to
/// test/output/design_export).
/// </summary>
public sealed partial class DesignExportTests : TestBase
{
    /// <summary>Test output.</summary>
    private readonly ITestOutputHelper _log;

    /// <summary>Output to test/output/design_export.</summary>
    public DesignExportTests(ITestOutputHelper log) : base("design_export") => _log = log;

    /// <summary>The fixture catalogue.</summary>
    private static FixtureCatalog Catalog => FixtureCatalog.Load(TestEnvironment.File(FixtureCatalog.DefaultPath));

    /// <summary>
    /// data/magicq_templates.shw holds, besides the lamps, a record MagicQ wrote of each kind an export may have to make
    /// in a show that has none: a cue (S), a cue stack (C), a group (G) and a palette of each type (F). With the
    /// environment variable KLOARTE_UPDATE_TEMPLATES=1 the missing ones are added from the Risquons-Tout shows (the
    /// smallest record of each kind).
    /// </summary>
    [Fact]
    public void TemplatesHoldTheRecordsAnExportMakes()
    {
        string path = TestEnvironment.File(MagicQShowEditor.TemplatesPath);
        var templates = MagicQShow.Load(path);
        if (Environment.GetEnvironmentVariable("KLOARTE_UPDATE_TEMPLATES") == "1")
        {
            var shows = Directory.EnumerateFiles(ShowFolder, "*RT2025_0*.shw", SearchOption.AllDirectories).OrderByDescending(f => f)
                .Concat(Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories).OrderBy(f => f)).ToList();
            foreach (string type in new[] { "S", "C", "G", "F9", "Fa", "Fb", "Fc" })
            {
                if (Has(templates, type))
                    continue;
                foreach (string shw in shows)
                {
                    var record = MagicQShow.Load(shw).Shw.Records.Where(r => Kind(r) == type).OrderBy(r => r.FieldCount).FirstOrDefault();
                    if (record == null)
                        continue;
                    templates.AddRecordFrom(record);
                    _log.WriteLine($"{type} from {Path.GetFileName(shw)}");
                    break;
                }
            }
            templates.Save(path);
            templates = MagicQShow.Load(path);
        }
        foreach (string type in new[] { "S", "C", "G", "F9", "Fa", "Fb", "Fc" })
            Assert.True(Has(templates, type), $"the templates have no {type} record");
    }

    /// <summary>The kind of a record: its type, for a palette with its type ("Fb").</summary>
    private static string Kind(ShwRecord record) =>
        record.Type == "F" && record.FieldCount > 2 ? "F" + record[1][0] : record.Type;

    /// <summary>True when the show has a record of the kind.</summary>
    private static bool Has(MagicQShow show, string kind) => show.Shw.Records.Any(r => Kind(r) == kind);
}
