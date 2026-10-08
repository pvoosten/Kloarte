using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>The MagicQ show as the model's show document: MagicVis items are the rig, S records the cues.</summary>
public partial class MagicQShow : IShowDocument
{
    /// <inheritdoc />
    IReadOnlyList<IRigItem> IRigDocument.RigItems => Vis;

    /// <inheritdoc />
    IEnumerable<ICue> IShowDocument.Cues => Cues;

    /// <inheritdoc />
    IReadOnlyList<ICueStack> IShowDocument.CueStacks => CueStacks;

    /// <summary>The .shw text and the .xhw's entries (names and sizes: Kloarte only adds or keeps entries).</summary>
    public long ContentHash()
    {
        long hash = Hashes.Fnv(Shw.ToText());
        foreach (var entry in Xhw?.Entries ?? new List<XhwArchive.Entry>())
            hash = Hashes.Add(Hashes.Add(hash, entry.Name), entry.Data.Length);
        return hash;
    }

    /// <summary>Head number of the L record at the address (0 = none).</summary>
    public int HeadAt(int address) => Patch.FirstOrDefault(p => p.Address == address)?.HeadNumber ?? 0;

    /// <summary>The address of every patched intensity channel (the personality's dimmer channel), in address order.</summary>
    public IReadOnlyList<int> IntensityAddresses =>
        Patch.Where(p => AttributeClass(this, p) == 'I').Select(p => p.Address).Distinct().OrderBy(a => a).ToList();

    /// <summary>Number of L records of the head.</summary>
    public int ChannelCountOfHead(int head) => Patch.Count(p => p.HeadNumber == head);

    /// <inheritdoc />
    IReadOnlyList<LampGroup> IShowDocument.Groups => Groups.Select(g => new LampGroup(g.Name, g.Heads)).ToList();

    /// <summary>
    /// Head name (L record at the lamp's address), personality (the H record's personality: name, channels) and the
    /// patch channel's gel, beam angle and shape.
    /// </summary>
    public LampPatch LampPatchOf(IRigItem lamp)
    {
        var vis = (VisItem)lamp;
        var personality = Personalities.FirstOrDefault(p => p.Id == vis.Personality);
        var channel = Patch.FirstOrDefault(p => p.Address == vis.Address);
        return new LampPatch
        {
            HeadName = HeadName(vis.Address),
            Personality = personality?.Name ?? "",
            ChannelCount = personality?.ChannelCount ?? 0,
            ChannelNames = personality?.Channels.Select(c => c.Name).ToList() ?? new List<string>(),
            GelId = channel?.GelOrNone ?? 0,
            BeamAngle = channel?.BeamAngle ?? 0,
            Shape = channel?.Shape ?? 0,
        };
    }

    /// <summary>The address plus the personality's intensity channel (<see cref="Personality.IntensityChannel"/>).</summary>
    public int IntensityAddressOf(int address)
    {
        var channel = Patch.FirstOrDefault(p => p.Address == address);
        if (channel == null)
            return 0;
        int offset = Personalities.FirstOrDefault(p => p.Id == channel.Personality)?.IntensityChannel ?? 0;
        return offset < 0 ? 0 : address + offset;
    }

    /// <summary>
    /// Adds a copy of a MagicVis record (H) with the next free id after the last H record, so a new item keeps the
    /// exact record layout of one MagicQ wrote.
    /// </summary>
    public IRigItem AddRigCopy(IRigItem template)
    {
        var source = (VisItem)template;
        int id = Vis.Max(v => v.Id) + 1;
        var record = Shw.InsertRecordAfter(Vis.Last().Record, source.Record.CopyLines());
        record.SetHex(1, id);
        var vis = new VisItem(record);
        Vis.Add(vis);
        return vis;
    }

    /// <summary>Removes a MagicVis item and its H record.</summary>
    public void RemoveRigItem(IRigItem item)
    {
        var vis = (VisItem)item;
        Shw.RemoveRecord(vis.Record);
        Vis.Remove(vis);
    }
}
