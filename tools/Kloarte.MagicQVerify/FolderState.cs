using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kloarte.MagicQVerify;

/// <summary>The files of a folder, to list what changed during a run.</summary>
internal static class FolderState
{
    /// <summary>Name -&gt; write time and size of every file.</summary>
    public static Dictionary<string, (DateTime Written, long Size)> Read(string folder) =>
        new DirectoryInfo(folder).GetFiles().ToDictionary(f => f.Name, f => (f.LastWriteTimeUtc, f.Length));

    /// <summary>The names of the files that are new or changed since `before`.</summary>
    public static List<string> Changed(string folder, Dictionary<string, (DateTime Written, long Size)> before) =>
        Read(folder).Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value)
            .Select(f => f.Key).Order().ToList();
}
