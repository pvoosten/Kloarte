using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Kloarte.Model;

/// <summary>
/// The readable names of lamp files and their header line (user decision of 11 Oct 2026): head number (decimal),
/// model, support and place, e.g. <c>h012-c103-LX1-3.yaml</c>. The name is derived (Kloarte renames the file on save
/// when these change); the key of a lamp file is its <see cref="LampFile.RigItem"/>, so a hand-renamed file still loads.
/// </summary>
public static class LampFileNames
{
    /// <summary>
    /// The file name of a lamp: <c>h&lt;head&gt;-&lt;model&gt;-&lt;support&gt;-&lt;place&gt;.yaml</c> (place = 1-based
    /// along its truss or stand; <c>free</c> without one). `model` is the catalogue id ("adb-c103" gives "c103").
    /// </summary>
    public static string For(IRigItem lamp, string model, Rig rig)
    {
        var (support, place) = Place(lamp, rig);
        string where = support == null ? "free" : $"{Clean(support.Name)}-{place}";
        return $"h{lamp.HeadNumber:000}-{Clean(ShortModel(model))}-{where}.yaml";
    }

    /// <summary>`name`, or "name-2.yaml", "name-3.yaml"... when `used` has it (case-insensitive); adds the result to `used`.</summary>
    public static string Unique(string name, ISet<string> used)
    {
        string stem = name[..^".yaml".Length], candidate = name;
        for (int n = 2; used.Contains(candidate); n++)
            candidate = $"{stem}-{n}.yaml";
        used.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// The header line of a lamp file, e.g. "Head 12 · DMX 1-012 · ADB C103 · L201 · LX1 #3 · MagicVis x -1.500 y 6.000
    /// z 2.100" (gel "O/W" when open white).
    /// </summary>
    public static string Header(IRigItem lamp, IShowDocument show, FixtureModel model, Rig rig)
    {
        var patch = show.LampPatchOf(lamp);
        var (support, place) = Place(lamp, rig);
        string N(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        var parts = new List<string>
        {
            $"Head {lamp.HeadNumber}",
            lamp.Address > 0 ? $"DMX {DmxAddress.Format(lamp.Address)}" : "not patched",
            model?.Name is { Length: > 0 } name ? name : patch.Personality,
            patch.GelId > 0 ? GelCodes.Code(patch.GelId) : "O/W",
            support == null ? "not on a truss or stand" : $"{support.Name} #{place}",
            $"MagicVis x {N(lamp.X)} y {N(lamp.Y)} z {N(lamp.Z)}",
        };
        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>The lamp's support and its 1-based place along it (from the support's start); (null, 0) without one.</summary>
    private static (RigSupport Support, int Place) Place(IRigItem lamp, Rig rig)
    {
        var support = rig?.Find(lamp.ParentId);
        if (support == null)
            return (null, 0);
        float Along(IRigItem l) => Vector3.Dot(new Vector3(l.X, l.Y, l.Z) - support.Start, support.Direction);
        var order = rig.LampsOn(support.Id).OrderBy(Along).ThenBy(l => l.Id).ToList();
        return (support, order.FindIndex(l => l.Id == lamp.Id) + 1);
    }

    /// <summary>The model id without its maker ("adb-c103" -> "c103", "robe-t1-profile" -> "t1-profile").</summary>
    private static string ShortModel(string model)
    {
        if (string.IsNullOrEmpty(model))
            return "lamp";
        int dash = model.IndexOf('-');
        return dash > 0 && dash < model.Length - 1 ? model[(dash + 1)..] : model;
    }

    /// <summary>Letters, digits, '-', '_' and '.' only (spaces and the rest dropped).</summary>
    private static string Clean(string text)
    {
        var s = new StringBuilder();
        foreach (char c in text ?? "")
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                s.Append(c);
        return s.Length > 0 ? s.ToString() : "x";
    }
}
