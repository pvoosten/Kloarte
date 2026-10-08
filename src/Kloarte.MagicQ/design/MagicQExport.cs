using System;
using System.IO;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// A native show (<see cref="DesignShow"/>) as a MagicQ show (decided 14 Oct 2026): written into a copy of the show it was
/// imported from (a new project: the new-show template, the house venue), changing only what differs: the patch, groups,
/// MagicVis items, palettes and the cue list's stack. Every record the design didn't change stays byte-identical (other
/// cue stacks, FX, curves, settings). A combined palette becomes one MagicQ palette per type it holds (their ids are
/// kept in the design for the next export). After writing, the file is imported again and compared with the design: a
/// difference is a warning. Never writes over an existing file.
/// </summary>
public static class MagicQExport
{
    /// <summary>
    /// Writes the show to `target` (which must not exist). New cues and palettes get their MagicQ ids in the design (so
    /// the design changes: save it). Returns what was written.
    /// </summary>
    public static ShowExport Export(DesignShow design, string target, FixtureCatalog catalog)
    {
        if (File.Exists(target))
            throw new IOException($"{target} exists already (an export never writes over a show)");
        string source = !string.IsNullOrEmpty(design.Rig.Source) && File.Exists(design.Rig.Source)
            ? design.Rig.Source : AppPaths.File(MagicQStore.NewShowTemplate);
        var show = MagicQShow.Load(source);
        var templates = MagicQShow.Load(AppPaths.File(MagicQShowEditor.TemplatesPath));
        var venue = MagicQShow.Load(AppPaths.File(MagicQStore.NewShowTemplate));
        var exporter = new MagicQExporter(design, show, templates, venue);
        exporter.Run();
        string folder = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        show.Save(target);
        exporter.Verify(target, catalog);
        design.Changed();
        return new ShowExport(target, exporter.Written, exporter.Warnings, exporter.CheckInConsole);
    }
}
