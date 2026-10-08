using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kloarte.Model;

/// <summary>The native show as project files: <c>rig.yaml</c>, <c>palettes.yaml</c> and <c>cues.yaml</c>.</summary>
public sealed partial class DesignShow : IProjectShow
{
    /// <summary>The rig and patch file (the show file of a native project).</summary>
    public const string RigFile = "rig.yaml";

    /// <summary>The palettes file.</summary>
    public const string PalettesFile = "palettes.yaml";

    /// <summary>The cue list file.</summary>
    public const string CuesFile = "cues.yaml";

    /// <summary>The cues file of a project of before (the cues of a MagicQ show: top-level "stacks:").</summary>
    private static readonly Regex ConsoleCues = new(@"(?m)^stacks:");

    /// <inheritdoc />
    public IReadOnlyList<string> ProjectFiles { get; } = new[] { RigFile, PalettesFile, CuesFile };

    /// <inheritdoc />
    public string TextOf(string file) => file switch
    {
        RigFile => FileCodec.Current.Write(Rig, ProjectSchemas.ModelineFor(ProjectSchemas.RigSchema, ""),
            new[] { FileText.HeaderLine("the rig and patch: fixture types, heads, groups, curves, lamps and objects") }),
        PalettesFile => FileCodec.Current.Write(PaletteList, ProjectSchemas.ModelineFor(ProjectSchemas.PalettesSchema, ""),
            new[] { FileText.HeaderLine("the palettes: values per head; cues refer to them as \"@name\"") }),
        CuesFile => FileCodec.Current.Write(CueList, ProjectSchemas.ModelineFor(ProjectSchemas.CueListSchema, ""),
            new[] { FileText.HeaderLine("the cue list, in cue order; a cue holds what it changes (tracking)") }),
        _ => throw new System.ArgumentException($"{file} is not a file of the show"),
    };

    /// <inheritdoc />
    public bool Apply(string file, string path, string text, List<FileProblem> problems)
    {
        switch (file)
        {
            case RigFile:
                var rig = FileCodec.Current.Read<DesignRig>(path, text);
                problems.AddRange(rig.Problems);
                if (rig.Value == null)
                    return false;
                Rig = rig.Value;
                break;
            case PalettesFile:
                var palettes = FileCodec.Current.Read<DesignPaletteList>(path, text);
                problems.AddRange(palettes.Problems);
                if (palettes.Value == null)
                    return false;
                PaletteList = palettes.Value;
                break;
            case CuesFile:
                if (ConsoleCues.IsMatch(text))
                {
                    problems.Add(new FileProblem(path, 0, 0,
                        "cues.yaml holds the cues of the MagicQ show of before: the next save writes the cue list over it", false));
                    return true;
                }
                var cues = FileCodec.Current.Read<DesignCueList>(path, text);
                problems.AddRange(cues.Problems);
                if (cues.Value == null)
                    return false;
                CueList = cues.Value;
                break;
            default:
                return false;
        }
        Changed();
        foreach (string problem in Problems().Where(p => file == CuesFile ? p.StartsWith("cue ") : file == PalettesFile ? p.StartsWith("palette ") : true))
            problems.Add(new FileProblem(path, 0, 0, problem + " (left out)", false));
        return true;
    }

    /// <summary>True when a cues file's text is the cues of a MagicQ show of before (a project of before), not a cue list.</summary>
    public static bool IsConsoleCues(string text) => ConsoleCues.IsMatch(text ?? "");

    /// <summary>
    /// A native show read from the project folder of its show file (<see cref="RigFile"/>): each file that reads, the
    /// others empty (their problems in `problems`).
    /// </summary>
    public static DesignShow LoadProject(string rigPath, List<FileProblem> problems)
    {
        var show = new DesignShow();
        string folder = Path.GetDirectoryName(Path.GetFullPath(rigPath)) ?? "";
        foreach (string file in show.ProjectFiles)
        {
            string path = Path.Combine(folder, file);
            if (File.Exists(path))
                show.Apply(file, path, File.ReadAllText(path), problems);
        }
        return show;
    }

    /// <summary>True when a file is a native show in one file (<see cref="Save"/>), not a console show or a project file.</summary>
    public static bool IsShowFile(string path)
    {
        if (!File.Exists(path))
            return false;
        using var reader = new StreamReader(path);
        for (int i = 0; i < 3 && reader.ReadLine() is { } line; i++)
            if (line.StartsWith("rig:"))
                return true;
        return false;
    }
}
