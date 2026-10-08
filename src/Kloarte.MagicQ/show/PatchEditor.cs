using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// Patch changes made from the plan view, written into the MagicQ show (saved with the show).
/// A lamp is a MagicVis fixture (H record); a head is the patch (L records, one per DMX channel). Several lamps can
/// be on one head (e.g. six lamps on one dimmer channel): they share its channel, so their intensity is always the
/// same (and the gel, which MagicQ stores per head). Operations:
/// <list type="bullet">
/// <item>a lamp to an existing head (same fixture type);</item>
/// <item>a lamp to a new head number at a new DMX address (copy of its old head's patch);</item>
/// <item>a head to another DMX address: its channels, its lamps, and every cue and palette value of those channels
///   move along (MagicQ does the same when repatching);</item>
/// <item>a head to another head number (also in the MagicQ groups).</item>
/// </list>
/// Conflicts are refused with a <see cref="PatchException"/>. No Godot dependencies.
/// </summary>
public sealed class PatchEditor
{
    /// <summary>Highest absolute DMX address (64 universes).</summary>

    private readonly MagicQShow _show;

    /// <summary>Creates a patch editor for a show.</summary>
    public PatchEditor(MagicQShow show) => _show = show;

    /// <summary>Patch channels of a head, in address order.</summary>
    public List<MagicQShow.PatchChannel> Channels(int headNumber) =>
        _show.Patch.Where(p => p.HeadNumber == headNumber).OrderBy(p => p.Address).ToList();

    /// <summary>Lamps (MagicVis fixtures) on a head.</summary>
    public List<MagicQShow.VisItem> Lamps(int headNumber) =>
        _show.Vis.Where(v => v.Kind == MagicQShow.VisKind.Fixture && v.HeadNumber == headNumber).ToList();

    /// <summary>The head whose channels include an address, or null.</summary>
    public int? HeadAt(int address) => _show.Patch.FirstOrDefault(p => p.Address == address)?.HeadNumber;

    /// <summary>Lets a lamp join an existing head: same address, same intensity (and the head's gel).</summary>
    private void AssignToHead(MagicQShow.VisItem lamp, int headNumber)
    {
        var channels = Channels(headNumber);
        if (channels.Count == 0)
            throw new PatchException($"Head {headNumber} is not patched");
        if (channels[0].Personality != lamp.Personality)
            throw new PatchException($"Head {headNumber} is a different fixture type");
        SetLampPatch(lamp, channels[0].Address, headNumber);
    }

    /// <summary>
    /// Gives a lamp a head of its own: a new head number at `address`, patched like the lamp's current head (fixture
    /// type, name, gel, shape, beam angle). The old head keeps its other lamps; a head without lamps stays patched
    /// (it is used in cues).
    /// </summary>
    private void NewHead(MagicQShow.VisItem lamp, int headNumber, int address)
    {
        if (Channels(headNumber).Count > 0)
            throw new PatchException($"Head number {headNumber} is already used; choose 'join head' to put the lamp on that head");
        var template = Channels(lamp.HeadNumber);
        if (template.Count == 0)
            throw new PatchException($"The lamp's head {lamp.HeadNumber} is not patched");
        CheckFree(address, template.Count, ignoreHead: null);
        int offset = address - template[0].Address;
        var after = _show.Patch.Last().Record;
        foreach (var ch in template)
        {
            var record = _show.Shw.InsertRecordAfter(after, ch.Record.CopyLines());
            var copy = new MagicQShow.PatchChannel(record);
            copy.Address = ch.Address + offset;
            copy.HeadNumber = headNumber;
            _show.Patch.Add(copy);
            after = record;
        }
        SetLampPatch(lamp, address, headNumber);
    }

    /// <summary>
    /// Repatches a head to another start address. Its lamps follow, and so does every cue value and palette value of
    /// its channels (MagicQ stores them by DMX address).
    /// </summary>
    private void MoveHead(int headNumber, int address)
    {
        var channels = Channels(headNumber);
        if (channels.Count == 0)
            throw new PatchException($"Head {headNumber} is not patched");
        CheckFree(address, channels.Count, ignoreHead: headNumber);
        int offset = address - channels[0].Address;
        if (offset == 0)
            return;
        var map = channels.ToDictionary(c => c.Address, c => c.Address + offset);
        foreach (var ch in channels)
            ch.Address = map[ch.Address];
        foreach (var lamp in Lamps(headNumber))
            lamp.Address = address;

        foreach (var cue in _show.Cues)
        {
            var moved = cue.Channels.Where(c => map.ContainsKey(c.Address)).ToList();
            foreach (var (a, _) in moved)
                cue.RemoveChannel(a);
            foreach (var (a, v) in moved)
                cue.SetChannel(map[a], v);
        }
        foreach (var palette in _show.Palettes)
            palette.MoveAddresses(map);
    }

    /// <summary>Renumbers a head (patch, lamps and the MagicQ groups that contain it).</summary>
    public void RenumberHead(int headNumber, int newNumber)
    {
        if (newNumber <= 0)
            throw new PatchException("Head numbers start at 1");
        if (Channels(newNumber).Count > 0)
            throw new PatchException($"Head number {newNumber} is already used; put the lamps on that head instead");
        var channels = Channels(headNumber);
        if (channels.Count == 0)
            throw new PatchException($"Head {headNumber} is not patched");
        foreach (var ch in channels)
            ch.HeadNumber = newNumber;
        foreach (var lamp in Lamps(headNumber))
            lamp.HeadNumber = newNumber;
        foreach (var group in _show.Groups)
            group.ReplaceHead(headNumber, newNumber);
    }

    /// <summary>
    /// Patches a newly added lamp (still on its template's head) as a head of its own: the next free head number at the
    /// first free DMX address after everything patched (not across a universe boundary), named `name`, open white and
    /// with the personality's default beam angle. Returns what it did.
    /// </summary>
    public string AddLampHead(MagicQShow.VisItem lamp, string name)
    {
        int count = Math.Max(1, Channels(lamp.HeadNumber).Count);
        int head = FreeHeadNumber();
        int address = FreeAddress(count);
        NewHead(lamp, head, address);
        foreach (var ch in Channels(head))
        {
            ch.HeadName = name;
            if (ch.GelOrNone != 0)
                ch.Gel = 0;
            if (ch.BeamAngle != 0)
                ch.BeamAngle = 0;
        }
        return $"Head {head} at DMX {DmxAddress.Format(address)}";
    }

    /// <summary>
    /// Removes the patch of a head when no lamp, cue, palette or group uses it any more (e.g. after removing a lamp
    /// that was just added). True when it was removed.
    /// </summary>
    public bool RemoveUnusedHead(int headNumber)
    {
        var channels = Channels(headNumber);
        if (channels.Count == 0 || Lamps(headNumber).Count > 0 || _show.Groups.Any(g => g.Heads.Contains(headNumber)))
            return false;
        var addresses = channels.Select(c => c.Address).ToHashSet();
        if (_show.Cues.Any(c => c.Channels.Any(ch => addresses.Contains(ch.Address)))
            || _show.Palettes.Any(p => p.Values.Any(v => addresses.Contains(v.Address))))
            return false;
        foreach (var ch in channels)
        {
            _show.Shw.RemoveRecord(ch.Record);
            _show.Patch.Remove(ch);
        }
        return true;
    }

    /// <summary>The first address after the highest patched channel with room for `count` channels in one universe.</summary>
    public int FreeAddress(int count)
    {
        int a = _show.Patch.Count == 0 ? 1 : _show.Patch.Max(p => p.Address) + 1;
        if ((a - 1) % 512 + count > 512)
            a = ((a - 1) / 512 + 1) * 512 + 1;
        return a;
    }

    /// <summary>A head number nobody uses yet (highest + 1).</summary>
    public int FreeHeadNumber() => _show.Patch.Count == 0 ? 1 : _show.Patch.Max(p => p.HeadNumber) + 1;

    /// <summary>
    /// Sets the head number and DMX address of one lamp, as typed in the plan view, and says what it did:
    /// <list type="bullet">
    /// <item>an address or head number of another existing head: the lamp joins that head;</item>
    /// <item>the only lamp on its head: the head is renumbered and / or moved to the new address;</item>
    /// <item>a lamp sharing its head with others and given a new address: it gets a head of its own (the given number, or
    ///   a free one when the number didn't change).</item>
    /// </list>
    /// Anything else is refused with a <see cref="PatchException"/> that explains why.
    /// </summary>
    public string ApplyLampPatch(MagicQShow.VisItem lamp, int head, int address)
    {
        int h0 = lamp.HeadNumber, a0 = lamp.Address;
        if (head == h0 && address == a0)
            return "No change";
        int? atAddress = address != a0 ? HeadAt(address) : null;
        if (atAddress is { } hx && hx != h0)
        {
            if (head != h0 && head != hx)
                throw new PatchException($"DMX {DmxAddress.Format(address)} is used by head {hx}");
            AssignToHead(lamp, hx);
            return $"Joined head {hx} at DMX {DmxAddress.Format(lamp.Address)}";
        }
        if (head != h0 && Channels(head).Count > 0)
        {
            int ha = Channels(head)[0].Address;
            if (address != a0 && address != ha)
                throw new PatchException($"Head {head} is at DMX {DmxAddress.Format(ha)}");
            AssignToHead(lamp, head);
            return $"Joined head {head} at DMX {DmxAddress.Format(ha)}";
        }
        bool alone = Lamps(h0).Count <= 1;
        if (alone)
        {
            var done = new List<string>();
            if (address != a0)
            {
                MoveHead(h0, address);
                done.Add($"moved to DMX {DmxAddress.Format(address)}");
            }
            if (head != h0)
            {
                RenumberHead(h0, head);
                done.Add($"renumbered to {head}");
            }
            return $"Head {h0} " + string.Join(" and ", done);
        }
        if (address == a0)
            throw new PatchException($"Head {h0} has {Lamps(h0).Count} lamps on DMX {DmxAddress.Format(a0)}: give this lamp another DMX " +
                                     "address to give it a head of its own, or type the number of an existing head to join it");
        int newHead = head != h0 ? head : FreeHeadNumber();
        NewHead(lamp, newHead, address);
        return $"New head {newHead} at DMX {DmxAddress.Format(address)}";
    }

    private static void SetLampPatch(MagicQShow.VisItem lamp, int address, int headNumber)
    {
        lamp.Address = address;
        lamp.HeadNumber = headNumber;
    }

    private void CheckFree(int address, int count, int? ignoreHead)
    {
        if (address < 1 || address + count - 1 > DmxAddress.Max)
            throw new PatchException($"DMX address {DmxAddress.Format(address)} is out of range");
        foreach (var p in _show.Patch)
            if (p.HeadNumber != ignoreHead && p.Address >= address && p.Address < address + count)
                throw new PatchException($"DMX {DmxAddress.Format(p.Address)} is used by head {p.HeadNumber} ({p.HeadName})");
    }
}
