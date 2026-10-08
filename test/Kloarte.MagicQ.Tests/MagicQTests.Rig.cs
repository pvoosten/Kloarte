using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Kloarte.MagicQ;
using Kloarte.Model;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.Rig.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>Patch changes from the plan (heads, addresses, joining and leaving heads).</summary>
    [Fact]
    public void PatchTest()
    {
        // RT2025_021: "Weverij T" heads 400-403, each one dimmer channel with six lamps.
        string src = Show("Risquons-Tout_RT2025_021.shw");
        var show = MagicQShow.Load(src);
        var p = new PatchEditor(show);
        Check(p.Channels(400).Count == 1 && p.Lamps(400).Count == 6, "Weverij T: head 400 = 1 channel, 6 lamps");

        // A lamp sharing head 400 gets a new address: a head of its own.
        var lamp = p.Lamps(400)[0];
        int free = Enumerable.Range(450, 50).First(a => p.HeadAt(a) == null);
        string msg = p.ApplyLampPatch(lamp, 400, free);
        Check(lamp.Address == free && lamp.HeadNumber != 400 && p.Lamps(400).Count == 5 && p.Channels(lamp.HeadNumber)[0].Gel == p.Channels(400)[0].Gel,
              $"split: {msg}");
        // ... and joins head 401 again by typing its number.
        msg = p.ApplyLampPatch(lamp, 401, lamp.Address);
        Check(lamp.HeadNumber == 401 && lamp.Address == p.Channels(401)[0].Address, $"join by head number: {msg}");
        // Typing an address of another head joins it.
        msg = p.ApplyLampPatch(lamp, lamp.HeadNumber, p.Channels(402)[0].Address);
        Check(lamp.HeadNumber == 402, $"join by address: {msg}");
        // Refused: an address used by another head while also asking for a different head number.
        Check(Throws(() => p.ApplyLampPatch(lamp, 403, p.Channels(400)[0].Address)), "conflicting head / address is refused");
        Check(Throws(() => p.ApplyLampPatch(lamp, 402, 999999)) || DmxAddress.Parse("999999") == null, "out of range is refused");

        // Moving a head moves its cue values and its lamps; renumbering updates the groups.
        var show2 = MagicQShow.Load(Show("RT2025_031.shw"));
        var p2 = new PatchEditor(show2);
        int head = 150, oldAddress = p2.Channels(head)[0].Address;
        var cuesWith = show2.Cues.Where(c => c.Channels.Any(ch => ch.Address == oldAddress)).Select(c => (c, v: c.Channels.First(ch => ch.Address == oldAddress).Value)).ToList();
        int target = Enumerable.Range(400, 100).First(a => p2.HeadAt(a) == null);
        string m2 = p2.ApplyLampPatch(p2.Lamps(head)[0], head, target);
        Check(p2.Channels(head)[0].Address == target && p2.Lamps(head).All(l => l.Address == target)
              && cuesWith.All(x => x.c.Channels.Any(ch => ch.Address == target && ch.Value == x.v) && x.c.Channels.All(ch => ch.Address != oldAddress)),
              $"{m2}: {cuesWith.Count} cues follow");
        var group = show2.Groups.First(g => g.Heads.Contains(head));
        int newNumber = p2.FreeHeadNumber();
        p2.RenumberHead(head, newNumber);
        Check(group.Heads.Contains(newNumber) && !group.Heads.Contains(head) && p2.Lamps(newNumber).Count > 0, $"renumbered to {newNumber}, group '{group.Name}' follows");

        // Only the touched records change.
        string dst = Path.Combine(_out, "patched.shw");
        show2.Save(dst);
        var oldRecords = MagicQShow.Load(Show("RT2025_031.shw")).Shw.Records.Select(Text).ToHashSet();
        var changed = MagicQShow.Load(dst).Shw.Records.Where(r => !oldRecords.Contains(Text(r))).Select(r => r.Type).Distinct().OrderBy(x => x).ToList();
        Check(changed.All(t => t is "L" or "H" or "S" or "G"), $"changed record types: {string.Join(",", changed)}");

        Check(DmxAddress.Parse("2-001") == 513 && DmxAddress.Parse("1.150") == 150 && DmxAddress.Parse("513") == 513
              && DmxAddress.Format(513) == "2-001" && DmxAddress.Parse("1-600") == null, "address text");
    }

    private static bool Throws(Action a)
    {
        try
        {
            a();
            return false;
        }
        catch (PatchException)
        {
            return true;
        }
    }
}
