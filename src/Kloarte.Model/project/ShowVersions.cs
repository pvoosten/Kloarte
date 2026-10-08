using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kloarte.Model;

/// <summary>
/// Numbered show files, as MagicQ's Save Show makes them: <c>Kruimels.shw</c>, <c>Kruimels_002.shw</c>,
/// <c>Kruimels_003.shw</c>... (a three-digit number after the last underscore; version 1 has none). Every save is a new file, so nothing is
/// overwritten; the base name (without the number) stays the same, and that is what ties the files to a project.
/// User: the project's show lives in the console's show root and Kloarte's saves take the next number too.
/// MagicQ adds a show's folder as a prefix when it saves a show from a subfolder into the root
/// (<c>Risquons-Tout\RT2025_002</c> -&gt; <c>Risquons-Tout_RT2025_003</c>); Kloarte's old "Kloarte" subfolder gave
/// <c>Kloarte_Kruimels_002</c>, <c>Kloarte_Kloarte_Kruimels_004</c>: such prefixes are not part of the name. MagicQ
/// also writes spaces as underscores (<c>la comedie</c> -&gt; <c>la_comedie_001</c>): names compare with spaces and
/// underscores alike, and Kloarte names its files the same way.
/// </summary>
public static class ShowVersions
{
    /// <summary>A trailing version number: "_" and three digits (MagicQ's; "_2026" is part of a name).</summary>
    private static readonly Regex Number = new(@"_(\d{3})$", RegexOptions.CultureInvariant);

    /// <summary>The prefix of Kloarte's old show subfolder (MagicQ added it on every save from there).</summary>
    private const string OldKloartePrefix = "Kloarte_";

    /// <summary>Splits a show name (file name without extension) into its base name and version (1 without a number).</summary>
    public static (string Base, int Version) Split(string name)
    {
        var match = Number.Match(name);
        return match.Success
            ? (name[..match.Index], int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            : (name, 1);
    }

    /// <summary>The base name of a show file (path or name): no extension, no version, no old Kloarte prefixes.</summary>
    public static string BaseOf(string file)
    {
        string name = Split(Path.GetFileNameWithoutExtension(file)).Base;
        while (name.StartsWith(OldKloartePrefix, StringComparison.OrdinalIgnoreCase) && name.Length > OldKloartePrefix.Length)
            name = name[OldKloartePrefix.Length..];
        return name;
    }

    /// <summary>The project name a show file suggests: its base name (<see cref="BaseOf"/>).</summary>
    public static string ProjectNameFor(string file) => BaseOf(file);

    /// <summary>
    /// True when a show file is a version of the project's show: the same base name, or the base name with the folder
    /// prefix MagicQ adds when it saves a show from that folder into the show root.
    /// </summary>
    public static bool IsVersionOf(string file, string projectName, string folderOfLaunchedShow = null)
    {
        string fileBase = BaseOf(file);
        string projectBase = BaseOf(projectName);
        return Same(fileBase, projectBase)
               || !string.IsNullOrEmpty(folderOfLaunchedShow) && Same(fileBase, folderOfLaunchedShow + "_" + projectBase);
    }

    /// <summary>True when two names are the same for MagicQ: case and spaces / underscores don't count.</summary>
    public static bool Same(string a, string b) =>
        string.Equals(a.Replace(' ', '_'), b.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The file the next save of the project's show goes to, in `folder`: <c>&lt;base&gt;.&lt;extension&gt;</c> when
    /// no version exists yet, else the base name with the number after the highest one in the folder and of the
    /// `current` file (MagicQ's three digits). Spaces become underscores, as in MagicQ. Never an existing file.
    /// </summary>
    public static string NextFile(string folder, string projectName, string current, string extension = ".shw")
    {
        string name = Split(projectName).Base.Replace(' ', '_');
        var versions = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*" + extension)
                .Select(f => Split(Path.GetFileNameWithoutExtension(f)))
                .Where(v => Same(v.Base, name))
                .Select(v => v.Version)
                .ToList()
            : new System.Collections.Generic.List<int>();
        if (!string.IsNullOrEmpty(current) && Same(BaseOf(current), name))
            versions.Add(Split(Path.GetFileNameWithoutExtension(current)).Version);
        int next = versions.Count == 0 ? 1 : versions.Max() + 1;
        while (true)
        {
            string path = Path.Combine(folder, (next == 1 ? name : $"{name}_{next:000}") + extension);
            if (!File.Exists(path))
                return path;
            next++;
        }
    }
}
