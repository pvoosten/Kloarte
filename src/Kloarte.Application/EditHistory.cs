using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// Undo / redo of every edit of the open show (UI review 6). Instead of an inverse per kind of edit (rig, patch, cues,
/// palettes, mechanics, blocking...), the history keeps snapshots: a <see cref="Checkpoint"/> after each settled edit
/// compares the fingerprints of show, mechanics and blocking with the last snapshot and, when they differ, records an
/// entry (before, after). Undo / redo put a snapshot back into the model; the engine side then rebuilds the scene from
/// the model. Edits can name their entry beforehand (<see cref="NameNextEdit"/>); without a name it says what changed.
/// Show snapshots are written with the console's own lossless save into a temp folder of this process.
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public sealed class EditHistory<TShow> where TShow : class, IShowDocument
{
    /// <summary>Most entries kept (older ones are dropped).</summary>
    public const int Limit = 100;

    /// <summary>The console's show files (saving and loading show snapshots).</summary>
    private readonly IShowStore<TShow> _store;

    /// <summary>The model now (null without a show).</summary>
    private readonly Func<ShowModel<TShow>> _model;

    /// <summary>Folder of the show snapshots (this process only).</summary>
    private readonly string _folder;

    /// <summary>Undo entries, oldest first.</summary>
    private readonly List<EditEntry> _undo = new();

    /// <summary>Redo entries, the next one last.</summary>
    private readonly List<EditEntry> _redo = new();

    /// <summary>The state at the last checkpoint (or undo / redo).</summary>
    private EditSnapshot _current;

    /// <summary>Name for the next recorded entry, or null.</summary>
    private string _nextName;

    /// <summary>Number of the next show snapshot file.</summary>
    private int _files;

    /// <summary>A history for the model `model` returns, with show snapshots in `folder` (made, emptied).</summary>
    public EditHistory(IShowStore<TShow> store, Func<ShowModel<TShow>> model, string folder)
    {
        _store = store;
        _model = model;
        _folder = folder;
        RemoveStaleFolders();
    }

    /// <summary>
    /// Removes the snapshot folders of Kloarte processes that are gone (folders named after a process id, next to this
    /// one); this process's own folder is emptied on <see cref="Reset"/>.
    /// </summary>
    private void RemoveStaleFolders()
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(_folder));
        if (parent == null || !Directory.Exists(parent))
            return;
        foreach (string dir in Directory.GetDirectories(parent))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid) || pid == Environment.ProcessId)
                continue;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                continue;   // still running
            }
            catch (ArgumentException)
            {
                // Gone: its snapshots are of no use any more.
            }
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for a later start.
            }
        }
    }

    /// <summary>The default snapshot folder: the temp folder, per process.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Path.GetTempPath(), "Kloarte", "undo", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Name of the entry Undo would undo, or null.</summary>
    public string UndoName => _undo.LastOrDefault()?.Name;

    /// <summary>Name of the entry Redo would redo, or null.</summary>
    public string RedoName => _redo.LastOrDefault()?.Name;

    /// <summary>Number of undo entries.</summary>
    public int Count => _undo.Count;

    /// <summary>Forgets every entry (a show was loaded) and takes the state now as the start.</summary>
    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        _nextName = null;
        _current = null;
        if (Directory.Exists(_folder))
            foreach (string file in Directory.GetFiles(_folder))
                TryDelete(file);
        _current = Take();
    }

    /// <summary>
    /// Takes the state now as the current one without an entry: after the engine filled in what the model leaves open
    /// (default mechanics after a rebuild), which is not an edit.
    /// </summary>
    public void Resync() => _current = Take();

    /// <summary>Names the next recorded edit ("Remove lamp"); the last name before a checkpoint wins.</summary>
    public void NameNextEdit(string name) => _nextName = name;

    /// <summary>
    /// Records an entry when the model changed since the last checkpoint (call it when an edit has settled: no drag
    /// running). Returns the entry's name, or null when nothing changed.
    /// </summary>
    public string Checkpoint()
    {
        var model = _model();
        if (model == null)
            return null;
        if (_current == null)
        {
            _current = Take();
            return null;
        }
        var now = Take();
        if (now.ShowHash == _current.ShowHash && now.Mechanics == _current.Mechanics && now.Blocking == _current.Blocking)
            return null;
        string name = _nextName ?? Describe(_current, now);
        _nextName = null;
        _undo.Add(new EditEntry(name, _current, now));
        if (_undo.Count > Limit)
            _undo.RemoveAt(0);
        _redo.Clear();
        _current = now;
        return name;
    }

    /// <summary>
    /// Undoes the last entry: the model gets the state before it (an edit not yet checkpointed is recorded first, so it
    /// is the one undone). Returns its name, or null when there is nothing to undo. The caller rebuilds the scene from
    /// the model and calls <see cref="Resync"/>.
    /// </summary>
    public string Undo()
    {
        Checkpoint();
        if (_undo.Count == 0)
            return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(entry.Before);
        _redo.Add(entry);
        return entry.Name;
    }

    /// <summary>Redoes the last undone entry (see <see cref="Undo"/>); null when there is nothing to redo.</summary>
    public string Redo()
    {
        if (Checkpoint() != null || _redo.Count == 0)
            return null;   // a new edit since the undo: redo is gone
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(entry.After);
        _undo.Add(entry);
        return entry.Name;
    }

    /// <summary>Puts a snapshot into the model (the show document only when it differs).</summary>
    private void Restore(EditSnapshot snapshot)
    {
        var model = _model();
        TShow show = model.Show.ContentHash() == snapshot.ShowHash ? null : _store.Load(snapshot.ShowFile);
        model.RestoreState(show, snapshot.Mechanics, snapshot.Blocking);
        _current = snapshot;
    }

    /// <summary>The state now; the show is written to a snapshot file only when it changed since the last one.</summary>
    private EditSnapshot Take()
    {
        var model = _model();
        if (model == null)
            return null;
        long hash = model.Show.ContentHash();
        string file = _current?.ShowHash == hash ? _current.ShowFile : WriteShow(model.Show);
        return new EditSnapshot
        {
            ShowHash = hash,
            ShowFile = file,
            Mechanics = model.MechanicsJson(),
            Blocking = model.BlockingJson(),
        };
    }

    /// <summary>Writes the show document as the next snapshot file.</summary>
    private string WriteShow(TShow show)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, $"snapshot_{_files++:0000}.show");
        show.Save(path);
        return path;
    }

    /// <summary>A name for an entry without one: what changed ("Show", "Settings of 2 lamps", "Blocking").</summary>
    private static string Describe(EditSnapshot before, EditSnapshot after)
    {
        var parts = new List<string>();
        if (before.ShowHash != after.ShowHash)
            parts.Add("Show (rig, patch or cues)");
        if (before.Mechanics != after.Mechanics)
        {
            int lamps = ChangedKeys(before.Mechanics, after.Mechanics);
            parts.Add(lamps == 1 ? "Settings of 1 lamp" : $"Settings of {lamps} lamps");
        }
        if (before.Blocking != after.Blocking)
            parts.Add("Blocking");
        return string.Join(", ", parts);
    }

    /// <summary>Number of keys whose value differs between two JSON objects (added and removed ones count).</summary>
    private static int ChangedKeys(string before, string after)
    {
        using var a = JsonDocument.Parse(before);
        using var b = JsonDocument.Parse(after);
        var left = a.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        var right = b.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        return left.Keys.Union(right.Keys).Count(k => left.GetValueOrDefault(k) != right.GetValueOrDefault(k));
    }

    /// <summary>Deletes a file; a file still in use stays.</summary>
    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next reset.
        }
    }
}
