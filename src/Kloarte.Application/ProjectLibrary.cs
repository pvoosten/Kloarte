using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// The projects folder (<see cref="AppPaths.Projects"/>): one folder per project (<see cref="KloarteProject"/>). Finds
/// the project of a show file and takes over the flat files of the time before projects
/// (&lt;projects&gt;/&lt;show name&gt;.mechanics.json, .blocking.json, .venue.json).
/// </summary>
public sealed class ProjectLibrary
{
    /// <summary>The project files that used to be flat, named after the show: &lt;show&gt;.&lt;kind&gt;.</summary>
    private static readonly string[] OldKinds = { "mechanics.json", "blocking.json", "venue.json" };

    /// <summary>A library on a projects folder (normalised: Godot gives paths with forward slashes).</summary>
    public ProjectLibrary(string root) => Root = Path.GetFullPath(root);

    /// <summary>The projects folder (absolute).</summary>
    public string Root { get; }

    /// <summary>Every project in the folder (unreadable documents are skipped), by name.</summary>
    public List<KloarteProject> All()
    {
        var projects = new List<KloarteProject>();
        if (!Directory.Exists(Root))
            return projects;
        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            string name = Path.GetFileName(folder);
            string document = new[] { Path.Combine(folder, name + KloarteProject.Extension),
                    Path.Combine(folder, name + KloarteProject.LegacyExtension) }
                .FirstOrDefault(File.Exists);
            if (document == null)
                continue;
            try
            {
                projects.Add(KloarteProject.Load(document));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                // Not a project (any more): leave it alone.
            }
        }
        return projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The project with this name, or null.</summary>
    public KloarteProject Find(string name) =>
        All().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The project a show file belongs to: the one whose show it is, else the one named after the show's base name
    /// (<see cref="ShowVersions.BaseOf"/>: a numbered save of its show), else a new project with that name (not written
    /// yet; the old flat files of that show are taken over, see <see cref="TakeOverOldFiles"/>).
    /// </summary>
    public KloarteProject ForShow(string showFile)
    {
        if (ExistingFor(showFile) is { } project)
            return project;
        string full = Path.GetFullPath(showFile);
        project = KloarteProject.Create(Root, ShowVersions.ProjectNameFor(full), full);
        TakeOverOldFiles(project);
        return project;
    }

    /// <summary>
    /// The existing project a show file belongs to (whose show it is, else named after its base name), or null.
    /// </summary>
    public KloarteProject ExistingFor(string showFile)
    {
        var all = All();
        string full = Path.GetFullPath(showFile);
        return all.FirstOrDefault(p => SamePath(p.ShowFile, full))
               ?? all.FirstOrDefault(p => ShowVersions.IsVersionOf(full, p.Name));
    }

    /// <summary>
    /// Copies the flat files from before projects (&lt;projects&gt;/&lt;show&gt;.mechanics.json, ...) of shows with the
    /// project's base name into the project folder, when the project has no such file yet. Of several (the show name
    /// drifted: Kruimels_001, Kloarte_Kruimels_002, ...) the newest one with content wins (a blocking with actors, a
    /// mechanics file with fixtures). The old files stay where they are. Returns the files copied.
    /// </summary>
    public List<string> TakeOverOldFiles(KloarteProject project)
    {
        var copied = new List<string>();
        if (!Directory.Exists(Root))
            return copied;
        foreach (string kind in OldKinds)
        {
            string target = project.FileOf(kind);
            if (File.Exists(target))
                continue;
            var best = Directory.EnumerateFiles(Root, "*." + kind)
                .Where(f => ShowVersions.Same(ShowVersions.BaseOf(Path.GetFileName(f)[..^(kind.Length + 1)]),
                    ShowVersions.BaseOf(project.Name)))
                .OrderByDescending(HasContent)
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (best == null)
                continue;
            Directory.CreateDirectory(project.Folder);
            File.Copy(best, target);
            copied.Add(best);
        }
        return copied;
    }

    /// <summary>
    /// True when an old project file holds something: fixtures (mechanics), actors (blocking), anything (venue).
    /// </summary>
    private static bool HasContent(string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (string list in new[] { "fixtures", "actors" })
                if (json.RootElement.TryGetProperty(list, out var items))
                    return items.ValueKind switch
                    {
                        JsonValueKind.Object => items.EnumerateObject().Any(),
                        JsonValueKind.Array => items.GetArrayLength() > 0,
                        _ => false,
                    };
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return false;
        }
    }

    /// <summary>True when two paths name the same file.</summary>
    private static bool SamePath(string a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a), b, StringComparison.OrdinalIgnoreCase);
}
