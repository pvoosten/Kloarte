using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// A project's folder as YAML files: the write and read rules shared by every file of the project (user decisions of
/// 11 Oct 2026). A file is written only when its content changed (<see cref="FileText"/>); a file that changed outside
/// Kloarte since it was last read or written is never overwritten silently (a conflict, the caller asks); a file that
/// doesn't parse is never overwritten at all. Kloarte remembers the content of each file it read or wrote
/// (<see cref="Remember"/>), so its own writes are not seen as external edits.
/// </summary>
public sealed class ProjectFolder
{
    /// <summary>The folder of the lamp files (one file per lamp).</summary>
    public const string MechanicsDirectory = "mechanics";

    /// <summary>Folder of the project's own gobos (PNG + drawing; their paths in the lamp files start with it).</summary>
    public const string GobosDirectory = "gobos";

    /// <summary>The folder of the blocking files (actors and steps).</summary>
    public const string BlockingDirectory = "blocking";

    /// <summary>The folder with the JSON Schemas the files' modelines point at (a copy per project).</summary>
    public const string SchemaDirectory = ".schemas";

    /// <summary>The content hash of each file as last read or written (full paths, case-insensitive).</summary>
    private readonly Dictionary<string, long> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A folder of the project (absolute path); the folder may not exist yet.</summary>
    public ProjectFolder(string root) => Root = Path.GetFullPath(root);

    /// <summary>The project folder (absolute).</summary>
    public string Root { get; }

    /// <summary>The lamp files' folder.</summary>
    public string MechanicsDir => Path.Combine(Root, MechanicsDirectory);

    /// <summary>The blocking files' folder.</summary>
    public string BlockingDir => Path.Combine(Root, BlockingDirectory);

    /// <summary>
    /// The full path of a file of the project folder, `name` with "/" or the system's separators ("blocking/actors.yaml").
    /// </summary>
    public string PathOf(string name) => Path.GetFullPath(Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>True when the project has YAML files (its folders exist): the project was saved as YAML.</summary>
    public bool HasYaml => Directory.Exists(MechanicsDir) || Directory.Exists(BlockingDir);

    /// <summary>The lamp files on disk (any name; the file's rig item id is what counts).</summary>
    public IEnumerable<string> LampFiles() =>
        Directory.Exists(MechanicsDir) ? Directory.EnumerateFiles(MechanicsDir, "*.yaml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

    /// <summary>The full path of a file (the key of the known contents).</summary>
    private static string Full(string path) => Path.GetFullPath(path);

    /// <summary>Counts the changes of what is known about the files (a cache of the unsaved check compares it).</summary>
    public int Version { get; private set; }

    /// <summary>Remembers the content of a file that was just read or written (no external change is pending for it).</summary>
    public void Remember(string path, string text)
    {
        _known[Full(path)] = FileText.Hash(text);
        Version++;
    }

    /// <summary>Forgets a file (it was deleted by Kloarte).</summary>
    public void Forget(string path)
    {
        _known.Remove(Full(path));
        Version++;
    }

    /// <summary>True when `text` is the content Kloarte last read or wrote for this file (an echo of its own write).</summary>
    public bool IsKnown(string path, string text) =>
        _known.TryGetValue(Full(path), out long known) && known == FileText.Hash(text);

    /// <summary>True when the file on disk is the content Kloarte last read or wrote (no external change pending).</summary>
    public bool IsUnchanged(string path)
    {
        if (!File.Exists(path))
            return false;
        return _known.TryGetValue(Full(path), out long known) && known == FileText.Hash(File.ReadAllText(path));
    }

    /// <summary>
    /// Writes Kloarte's text of a file when its content changed. Refused (a conflict, nothing written) when the file on
    /// disk changed outside Kloarte since it was last read or written, or doesn't parse as far as Kloarte knows (it was
    /// never read). `force` writes anyway (the user chose "Save Kloarte's first").
    /// </summary>
    public FileWrite Write(string path, string text, bool force = false)
    {
        string full = Full(path);
        if (File.Exists(full))
        {
            string disk = File.ReadAllText(full);
            if (FileText.Hash(disk) == FileText.Hash(text))
            {
                Remember(full, text);
                return new FileWrite(full, FileWriteKind.Unchanged, "");
            }
            if (!force && !IsUnchanged(full))
                return new FileWrite(full, FileWriteKind.Conflict, "changed outside Kloarte");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? Root);
        File.WriteAllText(full, text);
        Remember(full, text);
        return new FileWrite(full, FileWriteKind.Written, "");
    }

    /// <summary>
    /// Deletes a file Kloarte wrote (a lamp that was removed, or a renamed lamp's old file). Refused when the file
    /// changed outside Kloarte (unless `force`).
    /// </summary>
    public FileWrite Delete(string path, bool force = false)
    {
        string full = Full(path);
        if (!File.Exists(full))
            return new FileWrite(full, FileWriteKind.Unchanged, "");
        if (!force && !IsUnchanged(full))
            return new FileWrite(full, FileWriteKind.Conflict, "changed outside Kloarte");
        File.Delete(full);
        Forget(full);
        return new FileWrite(full, FileWriteKind.Deleted, "");
    }

    /// <summary>Reads a file as `T` (its text and hash are kept with the result; nothing is remembered yet).</summary>
    public FileRead<T> Read<T>(string path) where T : class
    {
        string full = Full(path);
        string text = File.ReadAllText(full);
        var (value, problems) = FileCodec.Current.Read<T>(full, text);
        return new FileRead<T>(full, text, value, problems);
    }

    /// <summary>
    /// Writes the JSON Schemas (<see cref="ProjectSchemas"/>) into the project's <c>.schemas</c> folder when they
    /// differ (the copy VS Code reads for the modelines).
    /// </summary>
    public void WriteSchemas()
    {
        string folder = Path.Combine(Root, SchemaDirectory);
        foreach (var (file, text) in ProjectSchemas.All())
        {
            string path = Path.Combine(folder, file);
            if (File.Exists(path) && File.ReadAllText(path) == text)
                continue;
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, text);
        }
    }
}
