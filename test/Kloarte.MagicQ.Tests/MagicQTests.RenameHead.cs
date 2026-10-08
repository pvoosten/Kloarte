using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: renaming a head (the name is kept in every patch channel of the head).</summary>
public partial class MagicQTests
{
    /// <summary>
    /// A head's name goes into every L record of that head and nothing else (other heads, other fields); it is cut to the
    /// console's 15 characters, quotes are dropped, a comma and accents survive a save and read back, and only
    /// the head's L lines differ in the file.
    /// </summary>
    [Fact]
    public void RenameHeadTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var editor = new MagicQShowEditor(show);
        var head = show.Patch.GroupBy(p => p.HeadNumber).First(g => g.Key > 0 && g.Count() > 1).Key;   // a head with several channels
        var others = show.Patch.Where(p => p.HeadNumber != head).Select(p => (p.Address, p.HeadName, p.HeadNumber, p.Gel)).ToList();
        int channels = show.Patch.Count(p => p.HeadNumber == head);

        Check(editor.RenameHead(head, "  Front, links \"é\"  ") == channels, "every channel of the head is renamed");
        Check(show.Patch.Where(p => p.HeadNumber == head).All(p => p.HeadName == "Front, links é"), "the name: trimmed, no quotes, comma and accent kept");
        Check(show.Patch.Where(p => p.HeadNumber != head).Select(p => (p.Address, p.HeadName, p.HeadNumber, p.Gel)).SequenceEqual(others),
              "other heads unchanged");
        Check(editor.RenameHead(head, "Front, links \"é\"") == 0, "the same name again changes nothing");

        editor.RenameHead(head, "A very long head name that does not fit");
        Check(show.Patch.First(p => p.HeadNumber == head).HeadName == "A very long hea", "cut to 15 characters");
        editor.RenameHead(head, "Front, links");
        string path = SaveCopy(show, "rename_head.shw");

        var reread = MagicQShow.Load(path);
        Check(reread.Patch.Where(p => p.HeadNumber == head).All(p => p.HeadName == "Front, links"), "saved and read back with its comma");
        var original = File.ReadAllLines(Show("RT2025_031.shw"));
        var saved = File.ReadAllLines(path);
        var changed = saved.Select((l, i) => (l, i)).Where(x => x.i >= original.Length || x.l != original[x.i]).ToList();
        Check(saved.Length == original.Length && changed.Count <= channels && changed.All(x => x.l.StartsWith("L,")),
              $"only the head's {channels} L lines differ ({changed.Count} changed)");
        Check(HeadNames.Clean("  \"x\"  ") == "x" && HeadNames.Clean(null) == "", "HeadNames.Clean");
    }
}
