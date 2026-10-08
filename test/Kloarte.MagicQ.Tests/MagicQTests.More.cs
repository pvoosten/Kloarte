using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.More.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>Art-Net packets: parsing, universes, offset.</summary>
    [Fact]
    public void ArtNetTest()
    {
        var r = new ArtNetReceiver();
        var ch = new byte[512];
        ch[0] = 10;
        ch[149] = 255;
        Check(r.Handle(ArtNetReceiver.Packet(1, ch)) && !r.Handle(new byte[] { 1, 2, 3 }), "ArtDmx packets parsed, others ignored");
        var snap = r.Snapshot();
        Check(snap[513] == 10 && snap[662] == 255 && !snap.ContainsKey(1), "Art-Net universe 1 = MagicQ universe 2 (addresses 513..1024)");
        r.UniverseOffset = 1;
        Check(r.Snapshot()[1] == 10, "universe offset");
    }
}
