using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kloarte.MagicQ;

/// <summary>
/// Starts MagicQ PC (mqqt.exe) with a show, outputting Art-Net to this PC. MagicQ ignores command-line arguments for
/// the show: it opens the show named in <c>Documents\MagicQ\show\status.dat</c> (its console state: the V + T records
/// of a show file) and takes its console settings (DMX I/O, IP address) from that file too. MagicQ rewrites the file
/// when it quits, so it is killed first and the file is edited while it is not running. No Godot dependencies.
/// </summary>
public static class MagicQPc
{
    /// <summary>MagicQ's process name.</summary>
    private const string ProcessName = "mqqt";

    /// <summary>Art-Net goes to this address: it exists on every PC, so nothing leaves the machine.</summary>
    public const string LocalIp = "127.0.0.1";

    /// <summary>mqqt.exe in the usual install folders, or null.</summary>
    public static string FindExecutable()
    {
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string exe = Path.Combine(Environment.GetFolderPath(root), "ChamSys Ltd", "MagicQ PC", "mqqt.exe");
            if (File.Exists(exe))
                return exe;
        }
        return null;
    }

    /// <summary>MagicQ's status file (Documents\MagicQ\show\status.dat).</summary>
    public static string StatusFile => Path.Combine(ShowFolderOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MagicQ", "show"), "status.dat");

    /// <summary>A show folder instead of MagicQ's own (tests that write shows; null = Documents\MagicQ\show).</summary>
    internal static string ShowFolderOverride { get; set; }

    /// <summary>MagicQ's show folder (Documents\MagicQ\show).</summary>
    public static string ShowFolder => Path.GetDirectoryName(StatusFile);

    /// <summary>
    /// The show MagicQ PC had loaded last (user: status.dat first): the show named in status.dat (the newer of its .shw
    /// and its .sbk autosave), else lastshow.shw (MagicQ's copy of it, unsaved changes included; written when MagicQ
    /// quits and not always present). Null when neither exists. A status.dat that can't be read (MagicQ writing it)
    /// counts as missing.
    /// </summary>
    public static string LastShowFile
    {
        get
        {
            try
            {
                var t = File.Exists(StatusFile) ? ShwDocument.Load(StatusFile).Records.FirstOrDefault(r => r.Type == "T") : null;
                if (t != null && t.FieldCount >= 2 && Existing(t.GetString(1)) is { } current)
                    return current;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                // status.dat is being rewritten by MagicQ: fall back to lastshow.shw.
            }
            string last = Path.Combine(ShowFolder, "lastshow.shw");
            return File.Exists(last) ? last : null;
        }
    }

    /// <summary>
    /// The show a file stands for: for lastshow.shw the show it is a copy of (its header), else the file itself.
    /// Mechanics, blocking and the venue are kept under that show's name, and MagicQ is started with it.
    /// </summary>
    public static string SourceOf(string shwPath)
    {
        if (!string.Equals(Path.GetFileName(shwPath), "lastshow.shw", StringComparison.OrdinalIgnoreCase))
            return shwPath;
        return Existing(MagicQShow.SavedAs(shwPath)) ?? shwPath;
    }

    /// <summary>
    /// The newest existing file of the show: the path itself, its .shw (saved) or its .sbk (MagicQ's autosave), by write
    /// time; null when none exists.
    /// </summary>
    internal static string Existing(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        return new[] { path, Path.ChangeExtension(path, ".shw"), Path.ChangeExtension(path, ".sbk") }
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>True while MagicQ PC runs.</summary>
    public static bool Running => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>
    /// Kills MagicQ PC if it runs (unsaved changes in MagicQ are lost), points status.dat at the show with Art-Net
    /// output on the given universes (1-based) to <see cref="LocalIp"/>, and starts MagicQ. The first time, the
    /// original status.dat is kept as status.dat.kloarte-backup. Returns a status line.
    /// </summary>
    public static string Launch(string showPath, IEnumerable<int> universes)
    {
        string exe = FindExecutable() ?? throw new FileNotFoundException("MagicQ PC (mqqt.exe) not found");
        string status = StatusFile;
        if (!File.Exists(status))
            throw new FileNotFoundException("MagicQ's status.dat not found: start MagicQ PC once first", status);
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            p.Kill();
            p.WaitForExit(10000);
        }
        string backup = status + ".kloarte-backup";
        if (!File.Exists(backup))
            File.Copy(status, backup);
        var doc = ShwDocument.Load(status);
        var outputs = PrepareStatus(doc, showPath, universes);
        doc.Save(status);
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
        return $"MagicQ PC started with {Path.GetFileName(showPath)}; Art-Net out on universe {string.Join(", ", outputs)} to {LocalIp}";
    }

    /// <summary>
    /// Edits MagicQ's console settings (the T record of status.dat or of a show): the show to open (f1), Art-Net output
    /// enabled on the given universes, and MagicQ's IP address <see cref="LocalIp"/> / 255.0.0.0. Returns the
    /// universes set. Universe words (after the count in f8): top byte = 0x80 enabled | out type (1 = Art-Net); the
    /// lower bytes (in type, Art-Net universe) are kept. The IP and mask follow the words (little-endian hex).
    /// </summary>
    public static List<int> PrepareStatus(ShwDocument doc, string showPath, IEnumerable<int> universes)
    {
        var t = doc.Records.FirstOrDefault(r => r.Type == "T") ?? throw new InvalidDataException("no T record");
        t.SetString(1, showPath.Replace('\\', '/'));
        int count = t.GetHex(8);
        var set = new List<int>();
        foreach (int u in universes.Distinct().Order())
        {
            if (u < 1 || u > count)
                continue;
            uint word = uint.Parse(t[8 + u], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            t[8 + u] = ((word & 0x00ffffffu) | 0x81000000u).ToString("x8", CultureInfo.InvariantCulture);
            set.Add(u);
        }
        t[9 + count] = IpHex(LocalIp);
        t[11 + count] = IpHex("255.0.0.0");
        return set;
    }

    /// <summary>MagicQ's IP notation: the four bytes little-endian as 8 hex digits (2.9.200.142 = 8ec80902).</summary>
    public static string IpHex(string dotted) =>
        string.Concat(dotted.Split('.').Reverse().Select(p => int.Parse(p, CultureInfo.InvariantCulture).ToString("x2")));
}
