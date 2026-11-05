using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

public sealed partial class DesignExportTests
{
    /// <summary>
    /// People (MagicVis objects of type 6) export with their code (H field 20: the kind of person), position and
    /// facing: new ones (copied from a person of the show) and an existing one whose code changed. The written show
    /// (test/output/design_export/people.shw) was checked in MagicQ PC's MagicVis: see docs/magicq-format.md.
    /// </summary>
    [Fact]
    public void PeopleExportWithTheirCode()
    {
        string shw = Show("Risquons-Tout_RT2025_044.shw");
        var source = MagicQShow.Load(shw);
        var design = MagicQImport.Import(source, shw, Catalog).Show;
        foreach (var p in source.Vis.Where(v => v.ObjectType == (int)RigObjectKind.Person))
            _log.WriteLine($"person {p.Id} {p.ObjectCode} \"{p.Name}\" at {p.X} {p.Y} {p.Z} rot {p.RotX} {p.RotY} {p.RotZ}");
        var existing = design.Rig.Items.First(i => i.Kind == "person" && i.Code == "60001");
        existing.Code = "60004";
        // A row downstage of the proscenium (MagicVis z 9.75), each kind once, facing the audience; one more turned.
        var added = new (string Code, double X, double RotY)[]
            { ("60001", -3, 0), ("60002", -1, 0), ("60003", 1, 0), ("60004", 3, 0), ("60001", 5, 90) };
        int first = design.NextItemId;
        for (int i = 0; i < added.Length; i++)
            design.Rig.Items.Add(new DesignItem
            {
                Id = first + i, Kind = "person", Code = added[i].Code, Name = $"K{i + 1} {added[i].Code}",
                At = new[] { added[i].X, 0, 11 }, Rot = new[] { 0, added[i].RotY, 0 }, Size = new[] { 0.0, 0, 0 },
            });
        design.Changed();

        string target = Target("people.shw");
        var export = MagicQExport.Export(design, target, Catalog);
        foreach (string line in export.Written.Concat(export.Warnings.Select(w => "WARNING " + w)))
            _log.WriteLine(line);
        Assert.DoesNotContain(export.Warnings, w => w.StartsWith("check after writing"));

        var written = MagicQShow.Load(target);
        Assert.Equal("60004", written.Vis.Single(v => v.Id == existing.Id).ObjectCode);
        for (int i = 0; i < added.Length; i++)
        {
            var person = written.Vis.Single(v => v.Id == first + i);
            Assert.Equal((int)RigObjectKind.Person, person.ObjectType);
            Assert.Equal(added[i].Code, person.ObjectCode);
            Assert.Equal($"K{i + 1} {added[i].Code}", person.Name);
            Assert.Equal(added[i].X, person.X, 2);
            Assert.Equal(11, person.Z, 2);
            Assert.Equal(added[i].RotY, person.RotY, 2);
        }
    }
}
