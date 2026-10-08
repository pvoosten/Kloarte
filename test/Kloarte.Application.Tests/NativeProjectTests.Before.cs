using System.IO;
using System.Linq;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

public sealed partial class NativeProjectTests
{
    /// <summary>
    /// The user's Kruimels project of before (15 Oct 2026, a copy, as it is: half converted by a save that refused its
    /// cues.yaml: rig.yaml and palettes.yaml written, the cues.yaml of the MagicQ show of before, the show still a MagicQ
    /// autosave .sbk). Opened, it is the imported show (with its cues); saved, every show file is written into the project
    /// and the project's show becomes its rig.yaml; opened again, the same cues.
    /// </summary>
    [Fact]
    public void KruimelsOfBeforeSavesItsCueList()
    {
        // Documents\Kloarte\Kruimels (Documents = the MagicQ show folder's grandparent: OneDrive's Documents here).
        string user = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(TestEnvironment.ShowFolder)), "Kloarte", "Kruimels");
        string sbk = Path.Combine(TestEnvironment.ShowFolder, "Kruimels_007.sbk");
        if (!Directory.Exists(user) || !File.Exists(sbk))
            return;
        string project = Path.Combine(_projects, "Kruimels");
        foreach (string file in Directory.EnumerateFiles(user, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(project, Path.GetRelativePath(user, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target);
        }
        string show = Path.Combine(_shows, "Kruimels_007.sbk");
        File.Copy(sbk, show);
        string document = Path.Combine(project, "Kruimels.kloarte.yaml");
        File.WriteAllText(document, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(document), @"(?m)^showFile: .*$", "showFile: " + show.Replace('\\', '/')));

        var session = Session();
        session.OpenProject("Kruimels");
        var model = session.Load();
        Assert.True(model.Show.CueOrder.Count > 5);
        string saved = session.SaveProject();
        Assert.DoesNotContain("Not saved", saved);
        Assert.DoesNotContain("stacks:", File.ReadAllText(Path.Combine(project, "cues.yaml")));
        Assert.Equal(Path.Combine(Path.GetFullPath(project), "rig.yaml"), session.ShowFile);
        Assert.Contains("rig.yaml", File.ReadAllText(document));
        Assert.Empty(session.UnsavedChanges());

        var again = Session();
        again.OpenProject("Kruimels");
        Assert.Equal(model.Show.ToText(), again.Load().Show.ToText());
    }
}
