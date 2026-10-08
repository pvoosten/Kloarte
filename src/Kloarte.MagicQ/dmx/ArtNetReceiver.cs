using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Kloarte.MagicQ;

/// <summary>
/// Receives Art-Net (ArtDmx packets on UDP 6454) on a background thread and keeps the last 512 channels of every
/// universe. <see cref="Snapshot"/> gives absolute DMX addresses as MagicQ numbers them: Art-Net universe 0 + the
/// offset = MagicQ universe 1 (address 1..512). No Godot dependencies.
/// </summary>
public sealed class ArtNetReceiver : IDisposable, IDmxInput
{
    /// <summary>The Art-Net UDP port.</summary>
    public const int Port = 6454;

    private readonly ConcurrentDictionary<int, byte[]> _universes = new();
    private UdpClient _udp;
    private Thread _thread;
    private volatile bool _running;

    /// <summary>Art-Net universe (15-bit port address) that is MagicQ universe 1.</summary>
    /// <remarks>Also <see cref="IDmxInput.UniverseOffset"/>.</remarks>
    public int UniverseOffset { get; set; }

    /// <summary>Number of ArtDmx packets received.</summary>
    public long Packets { get; private set; }

    /// <summary>When the last packet arrived (UTC), or MinValue.</summary>
    public DateTime LastPacket { get; private set; } = DateTime.MinValue;

    /// <summary>True while listening.</summary>
    public bool Running => _running;

    /// <summary>Starts listening (all interfaces, shared port so other receivers can listen too).</summary>
    public void Start(int port = Port)
    {
        if (_running)
            return;
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveTimeout = 250;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Art-Net receiver" };
        _thread.Start();
    }

    /// <inheritdoc />
    void IDmxInput.Start() => Start(Port);

    /// <summary>Stops listening.</summary>
    public void Stop()
    {
        _running = false;
        _udp?.Close();
        _thread?.Join(500);
        _udp = null;
        _thread = null;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void Loop()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                var data = _udp.Receive(ref from);
                Handle(data);
            }
            catch (SocketException)
            {
                // timeout: check _running
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    /// <summary>Parses one UDP payload; true when it was an ArtDmx packet.</summary>
    public bool Handle(byte[] p)
    {
        if (p.Length < 18 || p[0] != 'A' || p[1] != 'r' || p[2] != 't' || p[3] != '-' || p[7] != 0)
            return false;
        int opcode = p[8] | p[9] << 8;
        if (opcode != 0x5000)
            return false;
        int universe = p[14] | (p[15] & 0x7f) << 8;
        int length = Math.Min(p[16] << 8 | p[17], Math.Min(512, p.Length - 18));
        var buffer = _universes.GetOrAdd(universe, _ => new byte[512]);
        lock (buffer)
            Buffer.BlockCopy(p, 18, buffer, 0, length);
        Packets++;
        LastPacket = DateTime.UtcNow;
        return true;
    }

    /// <summary>All received channels as absolute addresses (1-based, MagicQ numbering) -&gt; 0..255.</summary>
    public Dictionary<int, int> Snapshot()
    {
        var result = new Dictionary<int, int>();
        foreach (var (universe, buffer) in _universes)
        {
            int magicQUniverse = universe - UniverseOffset;   // 0-based
            if (magicQUniverse < 0)
                continue;
            lock (buffer)
                for (int i = 0; i < 512; i++)
                    result[magicQUniverse * 512 + i + 1] = buffer[i];
        }
        return result;
    }

    /// <summary>An ArtDmx packet (for tests and senders): universe (15-bit), 512 channels.</summary>
    public static byte[] Packet(int universe, IReadOnlyList<byte> channels, byte sequence = 0)
    {
        var p = new byte[18 + 512];
        "Art-Net\0"u8.CopyTo(p);
        p[8] = 0x00;
        p[9] = 0x50;
        p[11] = 14;
        p[12] = sequence;
        p[14] = (byte)(universe & 0xff);
        p[15] = (byte)(universe >> 8 & 0x7f);
        p[16] = 0x02;
        p[17] = 0x00;
        for (int i = 0; i < Math.Min(512, channels.Count); i++)
            p[18 + i] = channels[i];
        return p;
    }
}
