using System;
using System.Linq;
using System.Text;

namespace Kloarte.Model;

/// <summary>
/// The content of a project file as Kloarte compares it: what it says, not the comments a person added. A file is
/// written only when its content changed, so hand-written comments survive until Kloarte changes that file (user
/// decision of 11 Oct 2026). The modeline and Kloarte's own header line count as content; other comment lines don't.
/// </summary>
public static class FileText
{
    /// <summary>The modeline (JSON Schema for VS Code) every written file starts with.</summary>
    public const string ModelinePrefix = "# yaml-language-server:";

    /// <summary>The start of Kloarte's header line (what the file is); a file with it is Kloarte's.</summary>
    public const string HeaderPrefix = "# Kloarte:";

    /// <summary>
    /// The content of a file's text: lines with trailing blanks removed, without the comment lines a person added
    /// (everything but the modeline and Kloarte's header), line ends "\n".
    /// </summary>
    public static string Content(string text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !l.TrimStart().StartsWith('#')
                        || l.StartsWith(ModelinePrefix, StringComparison.Ordinal)
                        || l.StartsWith(HeaderPrefix, StringComparison.Ordinal));
        return string.Join("\n", lines).Trim('\n');
    }

    /// <summary>The hash of a file's content (<see cref="Content"/>).</summary>
    public static long Hash(string text) => Hashes.Fnv(Content(text));

    /// <summary>True when a file's text is one Kloarte wrote (it has the header line).</summary>
    public static bool IsKloarte(string text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n').Any(l => l.StartsWith(HeaderPrefix, StringComparison.Ordinal));

    /// <summary>The header line of a file: what it is (one line, no line ends).</summary>
    public static string HeaderLine(string what) => HeaderPrefix + " " + what.Replace('\n', ' ').Trim();
}
