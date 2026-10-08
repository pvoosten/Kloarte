using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The native show of a project (decided 14 Oct 2026): its rig and patch (<see cref="Rig"/>, <c>rig.yaml</c>), palettes
/// (<c>palettes.yaml</c>) and one tracking cue list (<c>cues.yaml</c>), with values in physical units per head. It is a
/// show document like the console's (<see cref="IShowDocument"/>): the cues are projected to DMX (GDTF for the moving
/// heads), so playback, the timeline and the scene work on it as on a console show. A console show comes in by import and
/// goes out by export (the console binding). After a change made directly to the lists call <see cref="Changed"/> (the
/// projections are cached). No Godot or console dependencies.
/// </summary>
public sealed partial class DesignShow
{
    /// <summary>The rig and patch.</summary>
    public DesignRig Rig { get; private set; }

    /// <summary>The palettes.</summary>
    public DesignPaletteList PaletteList { get; private set; }

    /// <summary>The cue list.</summary>
    public DesignCueList CueList { get; private set; }

    /// <summary>An empty show.</summary>
    public DesignShow() : this(new DesignRig(), new DesignPaletteList(), new DesignCueList())
    {
    }

    /// <summary>A show of these parts (taken as they are).</summary>
    public DesignShow(DesignRig rig, DesignPaletteList palettes, DesignCueList cues)
    {
        Rig = rig ?? new DesignRig();
        PaletteList = palettes ?? new DesignPaletteList();
        CueList = cues ?? new DesignCueList();
        Normalize();
    }

    /// <summary>The palettes.</summary>
    public List<DesignPalette> Palettes => PaletteList.Palettes;

    /// <summary>The cues, in cue order.</summary>
    public List<DesignCue> CueOrder => CueList.Cues;

    /// <summary>Counts the changes (the projections are cached per version).</summary>
    public int Version { get; private set; }

    /// <summary>Layouts of the heads, by head number (this version).</summary>
    private readonly Dictionary<int, HeadLayout> _layouts = new();

    /// <summary>
    /// Takes a change made to the lists: the cues are put in number order, missing lists filled in, and the cached
    /// projections dropped.
    /// </summary>
    public void Changed()
    {
        Normalize();
        Version++;
        _layouts.Clear();
        _cueViews = null;
        _projections.Clear();
    }

    /// <summary>Fills in missing lists and sorts the cues by number (stable).</summary>
    private void Normalize()
    {
        Rig.Types ??= new();
        Rig.Heads ??= new();
        Rig.Groups ??= new();
        Rig.Curves ??= new();
        Rig.Items ??= new();
        PaletteList.Palettes ??= new();
        CueList.Cues ??= new();
        foreach (var cue in CueList.Cues)
            cue.Heads ??= new();
        foreach (var palette in PaletteList.Palettes)
            palette.Heads ??= new();
        var sorted = CueList.Cues.Select((c, i) => (c, i)).OrderBy(x => x.c.Number).ThenBy(x => x.i).Select(x => x.c).ToList();
        if (!sorted.SequenceEqual(CueList.Cues))
        {
            CueList.Cues.Clear();
            CueList.Cues.AddRange(sorted);
        }
    }

    /// <summary>The head with this number, or null.</summary>
    public DesignHead Head(int number) => Rig.Heads.FirstOrDefault(h => h.Number == number);

    /// <summary>The head one of whose channels is at a DMX address, or null.</summary>
    public DesignHead HeadCovering(int address) => Rig.Heads.FirstOrDefault(h => h.Covers(address));

    /// <summary>The fixture type with this name, or null.</summary>
    public DesignPersonality Type(string name) => Rig.Types.FirstOrDefault(t => t.Name == name);

    /// <summary>The palette with this name, or null.</summary>
    public DesignPalette Palette(string name) => Palettes.FirstOrDefault(p => p.Name == name);

    /// <summary>The cue with this id, or null.</summary>
    public DesignCue CueById(int id) => CueOrder.FirstOrDefault(c => c.Id == id);

    /// <summary>The next free cue id.</summary>
    public int NextCueId => CueOrder.Select(c => c.Id ?? 0).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The next free rig item id.</summary>
    public int NextItemId => Rig.Items.Select(i => i.Id).DefaultIfEmpty(0).Max() + 1;

    /// <summary>The channel layout of a head (cached until the next <see cref="Changed"/>).</summary>
    public HeadLayout LayoutOf(DesignHead head)
    {
        if (head == null)
            return null;
        if (_layouts.TryGetValue(head.Number, out var cached) && cached.Count == head.Channels)
            return cached;
        var type = Type(head.Type);
        var fixture = GdtfLibrary.Get(type?.Gdtf);
        var mode = GdtfLibrary.Mode(fixture, type?.GdtfMode);
        var layout = new HeadLayout(type, head.Channels, fixture, mode);
        _layouts[head.Number] = layout;
        return layout;
    }

    /// <summary>The layout of the head with this number, or null.</summary>
    public HeadLayout LayoutOf(int head) => LayoutOf(Head(head));

    /// <summary>The show in one file (shares the parts; write it, don't keep it).</summary>
    public DesignShowFile ToFile() => new() { Rig = Rig, Palettes = PaletteList, Cues = CueList };

    /// <summary>The show as the text of one file (the codec's).</summary>
    public string ToText() => FileCodec.Current.Write(ToFile(), null, new[] { FileText.HeaderLine("a native show (rig, palettes, cues)") });

    /// <summary>A fingerprint of everything the show holds.</summary>
    public long ContentHash() => Hashes.Fnv(FileCodec.Current.Write(ToFile(), null, Enumerable.Empty<string>()));

    /// <summary>Writes the show as one file.</summary>
    public void Save(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, ToText());
    }

    /// <summary>Reads a show written by <see cref="Save"/>. Throws <see cref="InvalidDataException"/> when it doesn't read.</summary>
    public static DesignShow Load(string path)
    {
        var read = FileCodec.Current.Read<DesignShowFile>(path, File.ReadAllText(path));
        if (read.Value == null)
            throw new InvalidDataException($"{path} is not a Kloarte show: {string.Join("; ", read.Problems)}");
        return new DesignShow(read.Value.Rig, read.Value.Palettes, read.Value.Cues);
    }

    /// <summary>A deep copy (through the file text).</summary>
    public DesignShow Copy()
    {
        var read = FileCodec.Current.Read<DesignShowFile>("copy", FileCodec.Current.Write(ToFile(), null, Enumerable.Empty<string>()));
        return new DesignShow(read.Value.Rig, read.Value.Palettes, read.Value.Cues);
    }

    /// <summary>A head number as a key in the files.</summary>
    public static string Key(int head) => head.ToString(CultureInfo.InvariantCulture);

    /// <summary>A head number from a key in the files, or null.</summary>
    public static int? HeadOfKey(string key) =>
        int.TryParse(key?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;

    /// <summary>Text of a number of a cue or a time ("3", "2.5").</summary>
    public static string Seconds(double? value) => (value ?? 0).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Puts every part back as another show has it (undo: the same object stays the model's show).</summary>
    public void ReplaceWith(DesignShow other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));
        Rig = other.Rig;
        PaletteList = other.PaletteList;
        CueList = other.CueList;
        Changed();
    }
}
