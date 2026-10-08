using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>The native show as a show document: the rig items, the cue list as one cue stack, the cues as DMX channels.</summary>
public sealed partial class DesignShow : IShowDocument
{
    /// <summary>The cues as the show document's cues (this version).</summary>
    private List<ICue> _cueViews;

    /// <inheritdoc />
    IReadOnlyList<IRigItem> IRigDocument.RigItems => Rig.Items;

    /// <summary>Adds a copy of a rig item with the next free id, after the last one.</summary>
    public IRigItem AddRigCopy(IRigItem template)
    {
        var copy = ((DesignItem)template).Copy();
        copy.Id = NextItemId;
        Rig.Items.Add(copy);
        Changed();
        return copy;
    }

    /// <summary>Removes a rig item.</summary>
    public void RemoveRigItem(IRigItem item)
    {
        Rig.Items.Remove((DesignItem)item);
        Changed();
    }

    /// <inheritdoc />
    IEnumerable<ICue> IShowDocument.Cues =>
        _cueViews ??= CueOrder.Where(c => c.Id != null).Select(c => (ICue)new DesignCueView(this, c)).ToList();

    /// <inheritdoc />
    IReadOnlyList<LampGroup> IShowDocument.Groups => Rig.Groups.Select(g => new LampGroup(g.Name, g.Heads ?? new int[0])).ToList();

    /// <inheritdoc />
    IReadOnlyList<ICueStack> IShowDocument.CueStacks => new ICueStack[] { new DesignStackView(this) };

    /// <summary>Head number of the head with a channel at the address (0 = none).</summary>
    public int HeadAt(int address) => HeadCovering(address)?.Number ?? 0;

    /// <summary>The address of every intensity channel of the patch, in address order.</summary>
    public IReadOnlyList<int> IntensityAddresses =>
        Rig.Heads.SelectMany(h => Enumerable.Range(0, h.Channels)
                .Where(o => (Type(h.Type)?.ClassAt(o) ?? 'I') == 'I')
                .Select(o => h.Address + o))
            .Distinct().OrderBy(a => a).ToList();

    /// <summary>Number of channels of the heads with this number.</summary>
    public int ChannelCountOfHead(int head) => Rig.Heads.Where(h => h.Number == head).Sum(h => h.Channels);

    /// <summary>The head's name, the fixture type's channels, and the head's gel, beam angle and shape.</summary>
    public LampPatch LampPatchOf(IRigItem lamp)
    {
        var item = (DesignItem)lamp;
        var head = item.Address is { } address ? HeadCovering(address) : null;
        var type = Type(item.Type ?? head?.Type);
        return new LampPatch
        {
            HeadName = head?.Name ?? "",
            Personality = type?.Name ?? item.Type ?? "",
            ChannelCount = type?.Channels.Count ?? head?.Channels ?? 0,
            ChannelNames = type?.Channels.ToList() ?? new List<string>(),
            GelId = head?.Gel ?? 0,
            BeamAngle = head?.Beam ?? 0,
            Shape = head?.Shape ?? 0,
        };
    }

    /// <summary>The address plus the fixture type's intensity channel (0 = no head there or no intensity).</summary>
    public int IntensityAddressOf(int address)
    {
        var head = HeadCovering(address);
        int offset = head == null ? -1 : Type(head.Type)?.IntensityOffset ?? 0;
        return offset < 0 ? 0 : address + offset;
    }

    /// <summary>The DMX value of a cue channel: the cues' channels are resolved already (<see cref="ChannelsOf(DesignCue)"/>).</summary>
    public int Resolve(int address, int rawValue) => rawValue & 0xff;

    /// <summary>The heads' dimmer curves applied to their intensity channels.</summary>
    public Dictionary<int, int> WithCurves(Dictionary<int, int> dmx)
    {
        Dictionary<int, int> result = null;
        foreach (var head in Rig.Heads.Where(h => h.Curve != null))
        {
            var curve = Rig.Curves.FirstOrDefault(c => c.Id == head.Curve);
            if (curve == null)
                continue;
            var type = Type(head.Type);
            for (int offset = 0; offset < head.Channels; offset++)
            {
                int address = head.Address + offset;
                if ((type?.ClassAt(offset) ?? 'I') != 'I' || !dmx.TryGetValue(address, out int value))
                    continue;
                result ??= new Dictionary<int, int>(dmx);
                result[address] = curve.Apply(value);
            }
        }
        return result ?? dmx;
    }
}
