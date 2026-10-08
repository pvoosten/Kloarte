using System.Collections.Generic;

namespace Kloarte.MagicQ;

/// <summary>DMX received from the console (MagicQ: Art-Net), buffered; read on the main thread.</summary>
public interface IDmxInput
{
    /// <summary>True while listening.</summary>
    bool Running { get; }

    /// <summary>The network universe received as the console's universe 1 (Art-Net: MagicQ's default 0).</summary>
    int UniverseOffset { get; set; }

    /// <summary>Number of DMX packets received.</summary>
    long Packets { get; }

    /// <summary>Starts listening.</summary>
    void Start();

    /// <summary>Stops listening.</summary>
    void Stop();

    /// <summary>The received values (absolute address -&gt; 0..255).</summary>
    Dictionary<int, int> Snapshot();

    /// <summary>Takes a packet as if it was received (tests, when another program holds the port). True when it was DMX.</summary>
    bool Handle(byte[] packet);
}
