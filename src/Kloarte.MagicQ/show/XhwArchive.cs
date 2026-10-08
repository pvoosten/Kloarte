using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

/// <summary>
/// MagicQ .xhw file: a ustar tar archive (older shows: gzip-compressed) with show assets: icons (icons/*.mc2)
/// and MagicVis models (models/*.mvm, binary, format unknown). Read and written by hand: an unchanged archive is
/// written back byte-identical; a changed one is rebuilt with MagicQ-style headers (and gzipped if it was).
/// </summary>
public sealed partial class XhwArchive
{

    private const int Block = 512;

    /// <summary>The files.</summary>
    public List<Entry> Entries { get; } = new();
    /// <summary>The archive was (and will be) gzip-compressed.</summary>
    private bool Gzipped { get; set; }
    /// <summary>end-of-archive marker (+ padding) as found in the file</summary>
    private int _trailingBytes = 2 * Block;
    private byte[] _originalFile;
    private List<Entry> _originalEntries = new();

    private bool Unchanged =>
        _originalFile != null && Entries.SequenceEqual(_originalEntries) && Entries.All(e => e.OriginalHeader != null);

    /// <summary>The entry with this name, or null.</summary>
    public Entry Find(string name) => Entries.FirstOrDefault(e => e.Name == name);

    /// <summary>Reads an .xhw.</summary>
    public static XhwArchive Load(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        var archive = new XhwArchive { _originalFile = file, Gzipped = file.Length > 2 && file[0] == 0x1f && file[1] == 0x8b };
        byte[] bytes = archive.Gzipped ? Gunzip(file) : file;
        int pos = 0;
        while (pos + Block <= bytes.Length && bytes.AsSpan(pos, Block).IndexOfAnyExcept((byte)0) >= 0)
        {
            var header = bytes.AsSpan(pos, Block).ToArray();
            string name = Ascii(header, 0, 100);
            string prefix = Ascii(header, 345, 155);
            if (prefix.Length > 0)
                name = prefix + "/" + name;
            int size = Convert.ToInt32(Ascii(header, 124, 12).Trim(), 8);
            var data = bytes.AsSpan(pos + Block, size).ToArray();
            archive.Entries.Add(new Entry { Name = name, Data = data, OriginalHeader = header });
            pos += Block + (size + Block - 1) / Block * Block;
        }
        archive._trailingBytes = bytes.Length - pos;
        archive._originalEntries = archive.Entries.ToList();
        return archive;
    }

    /// <summary>Writes the .xhw (unchanged = original bytes).</summary>
    public void Save(string path)
    {
        if (Unchanged)
        {
            File.WriteAllBytes(path, _originalFile);
            return;
        }
        using var file = File.Create(path);
        using var gzip = Gzipped ? new GZipStream(file, CompressionLevel.Optimal) : null;
        Stream stream = gzip ?? (Stream)file;
        foreach (var entry in Entries)
        {
            stream.Write(entry.OriginalHeader ?? CreateHeader(entry));
            stream.Write(entry.Data);
            int pad = (Block - entry.Data.Length % Block) % Block;
            stream.Write(new byte[pad]);
        }
        stream.Write(new byte[Math.Max(_trailingBytes, 2 * Block)]);
    }

    /// <summary>Same layout as MagicQ's own headers: mode 000644, uid/gid 0, mtime 0, ustar magic.</summary>
    private static byte[] CreateHeader(Entry entry)
    {
        if (Encoding.ASCII.GetByteCount(entry.Name) > 100)
            throw new ArgumentException($"xhw entry name too long: {entry.Name}");
        var h = new byte[Block];
        Put(h, 0, entry.Name);
        Put(h, 100, "000644 \0");
        Put(h, 108, "000000 \0");
        Put(h, 116, "000000 \0");
        Put(h, 124, Convert.ToString(entry.Data.Length, 8).PadLeft(11, '0') + " ");
        Put(h, 136, "00000000000 ");
        Put(h, 148, "        ");
        h[156] = (byte)'0';
        Put(h, 257, "ustar\0" + "00");
        Put(h, 329, "000000 \0");
        Put(h, 337, "000000 \0");
        int sum = h.Sum(b => (int)b);
        Put(h, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        return h;
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var result = new MemoryStream();
        gzip.CopyTo(result);
        return result.ToArray();
    }

    private static void Put(byte[] buffer, int offset, string s) => Encoding.ASCII.GetBytes(s).CopyTo(buffer, offset);

    private static string Ascii(byte[] buffer, int offset, int length)
    {
        int end = Array.IndexOf(buffer, (byte)0, offset, length);
        return Encoding.ASCII.GetString(buffer, offset, (end < 0 ? offset + length : end) - offset);
    }
}
