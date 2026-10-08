using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The file system watcher of a project folder (<see cref="IProjectWatcher"/>): every YAML file in it and its subfolders.
/// The events come on a thread of the system; they only record the time of the last change, and
/// <see cref="TakeSettled"/> reports the files that have been quiet for <see cref="Quiet"/>.
/// </summary>
public sealed class ProjectFolderWatcher : IProjectWatcher
{
    /// <summary>How long a file must be unchanged before it is reported (editors write a file in several steps).</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);

    /// <summary>The watcher of the folder (events are recorded here).</summary>
    private readonly FileSystemWatcher _watcher;

    /// <summary>The last change of each file (full path, UTC time).</summary>
    private readonly ConcurrentDictionary<string, DateTime> _changed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Watches `folder` (it must exist) for YAML files.</summary>
    public ProjectFolderWatcher(string folder)
    {
        _watcher = new FileSystemWatcher(folder, "*.yaml")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += (_, e) => Record(e.FullPath);
        _watcher.Created += (_, e) => Record(e.FullPath);
        _watcher.Renamed += (_, e) => Record(e.FullPath);
        _watcher.Deleted += (_, e) => Record(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> TakeSettled()
    {
        var now = DateTime.UtcNow;
        var settled = _changed.Where(c => now - c.Value >= Quiet).Select(c => c.Key).ToList();
        foreach (string path in settled)
            _changed.TryRemove(path, out _);
        return settled;
    }

    /// <summary>Notes a change of a file now.</summary>
    private void Record(string path) => _changed[Path.GetFullPath(path)] = DateTime.UtcNow;

    /// <inheritdoc />
    public void Dispose() => _watcher.Dispose();
}
