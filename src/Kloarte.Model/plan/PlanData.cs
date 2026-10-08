using System.Collections.Generic;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Everything the plan (top view) shows, as a snapshot in plan coordinates (stage x, stage z; metres): stage outline,
/// cyc, scenery footprints, trusses and stands, lamps, seats and title information. Made by ShowBuilder.PlanData,
/// drawn by PlanDrawing on screen and into the PDF. No Godot dependencies.
/// </summary>
public sealed class PlanData
{
    /// <summary>Show file name.</summary>
    public string Show { get; init; } = "";

    /// <summary>Venue name.</summary>
    public string Venue { get; init; } = "";

    /// <summary>Stage edge z (downstage end of the forestage).</summary>
    public float StageEdge { get; init; }

    /// <summary>Back of the stage (cyc / back wall) z.</summary>
    public float StageBack { get; init; }

    /// <summary>Stage width.</summary>
    public float StageWidth { get; init; } = 15f;

    /// <summary>Cyc width (0 = none).</summary>
    public float CycWidth { get; init; }

    /// <summary>Scenery footprints.</summary>
    public List<PlanShape> Shapes { get; } = new();

    /// <summary>Trusses, pipes and stands.</summary>
    public List<PlanSupport> Supports { get; } = new();

    /// <summary>Lamps.</summary>
    public List<PlanLamp> Lamps { get; } = new();

    /// <summary>Seats (position, reserved for the control position).</summary>
    public List<(Vector2 Position, bool Reserved)> Seats { get; } = new();

    /// <summary>Seat width (for drawing the seats).</summary>
    public float SeatWidth { get; init; } = 0.5f;
}
