namespace Kloarte.Model;

/// <summary>
/// What a moving head does, decoded from its DMX channels (GdtfDecoder): position, colour, beam. Angles in degrees.
/// No Godot dependencies.
/// </summary>
public sealed class MovingHeadState
{
    /// <summary>Pan (degrees; 0 = DMX centre).</summary>
    public float Pan { get; set; }

    /// <summary>Tilt (degrees; 0 = straight along the mounting axis).</summary>
    public float Tilt { get; set; }

    /// <summary>Colour filter / mix, red 0..1 (white = open).</summary>
    public float R { get; set; } = 1;

    /// <summary>Colour, green 0..1.</summary>
    public float G { get; set; } = 1;

    /// <summary>Colour, blue 0..1.</summary>
    public float B { get; set; } = 1;

    /// <summary>Zoom: field angle in degrees (0 = no zoom channel).</summary>
    public float Zoom { get; set; }

    /// <summary>Focus 0..1 (0.5 = sharp at a typical throw).</summary>
    public float Focus { get; set; } = 0.5f;

    /// <summary>Frost 0..1.</summary>
    public float Frost { get; set; }

    /// <summary>Iris 0..1 (1 = open).</summary>
    public float Iris { get; set; } = 1f;

    /// <summary>Gobo wheel slot (1 = open, 0 = no gobo wheel) and its image file name.</summary>
    public int GoboSlot { get; set; }

    /// <summary>Image (media file name in the GDTF) of the gobo in the beam, or "".</summary>
    public string GoboMedia { get; set; } = "";

    /// <summary>Gobo rotation (degrees).</summary>
    public float GoboRotation { get; set; }

    /// <summary>Framing blades 1..4 (top, right, bottom, left): insertion 0..1 of the GDTF range and rotation.</summary>
    public float[] BladeIn { get; } = new float[4];

    /// <summary>Blade rotations (degrees).</summary>
    public float[] BladeRotation { get; } = new float[4];

    /// <summary>Rotation of the whole blade assembly (degrees).</summary>
    public float ShaperRotation { get; set; }

    /// <summary>False when the shutter channel is closed (no light).</summary>
    public bool ShutterOpen { get; set; } = true;
}
