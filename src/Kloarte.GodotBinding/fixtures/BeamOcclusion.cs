using System.Collections.Generic;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Occlusion of haze beams by scenery: the switch, the settings and the queue that spreads the lamps' occlusion maps
/// over frames. Every lamp (<see cref="ProfileFixture"/>, also in a moving head) has a small depth view from its lens
/// that sees only <see cref="RenderLayers.Occluders"/>; <c>beam_volume.gdshader</c> skips the haze behind what it sees.
/// A map is rendered only when it is out of date (aim, zoom, the lamp's beam length,
/// <see cref="ProfileFixture.InvalidateOcclusion"/>) and the beam is visible, at most <see cref="PerFrame"/> maps per
/// frame in the order asked for, so a rebuild of a large rig doesn't stall.
/// </summary>
public static class BeamOcclusion
{
    /// <summary>
    /// Beams are cut by scenery (on by default: +5.5 % frame time on the Kruimels rig, docs/godot-findings.md 12). Off:
    /// the beam shader ignores the maps and none are rendered.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Occlusion maps rendered per frame at most.</summary>
    internal static int PerFrame { get; set; } = 4;

    /// <summary>Size (pixels) of a lamp's square occlusion map.</summary>
    internal const int Resolution = 128;

    /// <summary>
    /// A lamp that keeps moving (a moving head during a pan / tilt fade) renders its map at most once in this many
    /// frames; its last position is always rendered.
    /// </summary>
    internal static int MovingInterval { get; set; } = 4;

    /// <summary>Maps rendered since the start (for tests and measurements).</summary>
    internal static int Rendered { get; private set; }

    /// <summary>Number of lamps waiting for their map.</summary>
    internal static int Pending => Queue.Count;

    /// <summary>Lamps waiting, in the order they asked.</summary>
    private static readonly Queue<ProfileFixture> Queue = new();

    /// <summary>The lamps in <see cref="Queue"/> (each only once).</summary>
    private static readonly HashSet<ProfileFixture> Queued = new();

    /// <summary>Process frame in which the queue was last served.</summary>
    private static ulong _servedFrame = ulong.MaxValue;

    /// <summary>Puts a lamp in the queue (once).</summary>
    internal static void Request(ProfileFixture lamp)
    {
        if (Queued.Add(lamp))
            Queue.Enqueue(lamp);
    }

    /// <summary>
    /// Serves the queue once per process frame: the first lamp to call it in a frame starts the renders of the next
    /// <see cref="PerFrame"/> lamps.
    /// </summary>
    internal static void Pump()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _servedFrame)
            return;
        _servedFrame = frame;
        for (int n = 0; n < PerFrame && Queue.Count > 0;)
        {
            var lamp = Queue.Dequeue();
            Queued.Remove(lamp);
            if (!GodotObject.IsInstanceValid(lamp) || !lamp.IsInsideTree())
                continue;
            if (lamp.RenderOcclusion())
            {
                Rendered++;
                n++;
            }
        }
    }
}
