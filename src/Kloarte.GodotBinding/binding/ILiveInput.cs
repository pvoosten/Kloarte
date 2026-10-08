using System.Collections.Generic;

namespace Kloarte.GodotBinding;

/// <summary>Live DMX from the console (Art-Net), as the views use it.</summary>
public interface ILiveInput
{
    /// <summary>True while listening.</summary>
    bool Running { get; }

    /// <summary>The network universe received as the console's universe 1 (a preference).</summary>
    int UniverseOffset { get; set; }

    /// <summary>Number of DMX packets received.</summary>
    long Packets { get; }

    /// <summary>Starts listening; throws <see cref="System.Net.Sockets.SocketException"/> when the port can't be used.</summary>
    void Start();

    /// <summary>Stops listening.</summary>
    void Stop();

    /// <summary>The received values (absolute address -&gt; 0..255).</summary>
    Dictionary<int, int> Snapshot();

    /// <summary>Takes a packet as if it was received (tests, when another program holds the port). True when it was DMX.</summary>
    bool Receive(byte[] packet);
}
