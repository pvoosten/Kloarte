using System.IO;
using System.Linq;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// New lamps never take a rig item id a lamp file of the project still names (user, 25 Oct 2026: a new LEDBeam got id 197,
/// the id of a DS104's file that stayed after the DS104 went; that file would have become the LEDBeam's mechanics).
/// </summary>
public sealed partial class NativeProjectTests
{
    /// <summary>A lamp file of a lamp that is gone (at load, and one that comes in later) keeps its id from new lamps.</summary>
    [Fact]
    public void NewLampsSkipIdsOfLampFiles()
    {
        var session = Session();
        session.NewProject("Ids");
        session.Load();
        session.SaveProject();
        string mechanics = Path.Combine(session.ProjectFolder, "mechanics");
        Directory.CreateDirectory(mechanics);
        File.WriteAllText(Path.Combine(mechanics, "h099-old.yaml"), "rigItem: 500\nmodel: adb-ds104\n");

        var again = Session();
        again.OpenProject("Ids");
        var model = again.Load();
        Assert.Contains(again.LoadProblems, p => p.Message.Contains("no lamp 500"));
        var lamp = (DesignItem)again.Editor.ImportLamp(DesignTypes.GenericName, "New", "PC");
        Assert.Equal(501, lamp.Id);

        // A lamp file that comes in while the project is open (an outside edit) counts too.
        string later = Path.Combine(mechanics, "h098-later.yaml");
        File.WriteAllText(later, "rigItem: 600\nmodel: adb-c103\n");
        _watcher.Settled.Add(later);
        again.PollProjectChanges();
        Assert.Equal(601, ((DesignItem)again.Editor.ImportLamp(DesignTypes.GenericName, "Newer", "PC")).Id);
        Assert.DoesNotContain(model.Show.Rig.Items, i => i.Id is 500 or 600);
    }
}
