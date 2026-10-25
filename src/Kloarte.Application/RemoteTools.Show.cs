using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>RemoteTools: the open show as a whole: its state, its files' problems, undo / redo, save and the export.</summary>
public sealed partial class RemoteTools
{
    /// <summary>The tools of the show as a whole.</summary>
    private IEnumerable<RemoteTool> ShowTools()
    {
        yield return Tool("get_status", "Kloarte status",
            "What Kloarte has open: project, files, unsaved parts, undo / redo, the current cue, haze, counts of heads, " +
            "lamps, cues and palettes, the views and the selected lamps. Call this first.",
            Object(), readOnly: true, _ => Status());

        yield return Tool("get_problems", "File problems",
            "Errors and warnings in the project's YAML files found at the last load (file, line, column). A broken file " +
            "is never applied: Kloarte keeps its last good state.",
            Object(), readOnly: true, _ => Files.LoadProblems.Select(p => new
            {
                file = p.Path, line = p.Line, column = p.Column, message = p.Message, error = p.IsError,
            }).ToList());

        yield return AsyncTool("undo", "Undo",
            "Undoes the last edit in Kloarte (as Ctrl+Z, also edits made in the UI).",
            Object(), readOnly: false, async _ =>
                await _app.Undo() is { } name ? Done($"Undone: {name}") : "Nothing to undo");

        yield return AsyncTool("redo", "Redo", "Redoes the last undone edit.",
            Object(), readOnly: false, async _ =>
                await _app.Redo() is { } name ? Done($"Redone: {name}") : "Nothing to redo");

        yield return Tool("save_project", "Save project",
            "Saves the project as File > Save does: the YAML files that changed (rig, palettes, cues, mechanics, " +
            "blocking). Hand comments in the files survive.",
            Object(), readOnly: false, _ => Done(_app.Save()));

        yield return Tool("export_magicq", "Export to MagicQ",
            "Exports the show for MagicQ as the project's next numbered show in MagicQ's show folder (never an " +
            "overwrite; only what differs from the source show is written). Returns what was written and what to check.",
            Object(), readOnly: false, _ => Done(_app.ExportConsoleShow()));
    }

    /// <summary>The answer of get_status.</summary>
    private object Status()
    {
        var files = _app.Files;
        var model = files?.Model;
        var show = _app.Design;
        var current = show != null && _app.CueId is { } id ? show.CueById(id) : null;
        int cue = current == null ? -1 : show.CueOrder.IndexOf(current);
        return new
        {
            project = model == null ? null : files.ProjectName,
            folder = model == null ? null : files.ProjectFolder,
            showFile = model == null ? null : files.ShowFile,
            loaded = model != null,
            native = show != null,
            unsaved = model == null ? null : files.UnsavedChanges(),
            undo = files?.UndoName,
            redo = files?.RedoName,
            currentCue = current == null ? null : new { cue = current.Cue, id = current.Id, text = current.Text, index = cue },
            fading = _app.Running,
            hazePercent = System.Math.Round(ProjectLook.PercentOf(_app.Haze), 1),
            strayLight = System.Math.Round(_app.StrayLight, 2),
            heads = show?.Rig.Heads.Count,
            lamps = model?.Show.RigItems.Count(i => i.IsFixture),
            cues = show?.CueOrder.Count,
            palettes = show?.Palettes.Count,
            problems = model == null ? null : (int?)files.LoadProblems.Count,
            views = _app.Views,
            mainView = _app.MainView,
            selectedLamps = _app.SelectedLamps,
        };
    }
}
