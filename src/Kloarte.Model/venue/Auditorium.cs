using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>Auditorium: rows of seats behind the stage edge, rising by Rake per row.</summary>
public sealed class Auditorium
{
    /// <summary>from the stage edge to the first row (seat front)</summary>
    public float FirstRowDistance { get; set; } = 0.5f;
    /// <summary>first row floor below the stage floor</summary>
    public float FirstRowDrop { get; set; } = 1f;
    /// <summary>Distance between rows (m).</summary>
    public float RowPitch { get; set; } = 1f;
    /// <summary>rise per row</summary>
    public float Rake { get; set; } = 0.27f;
    /// <summary>Seat width (m).</summary>
    public float SeatWidth { get; set; } = 0.52f;
    /// <summary>seated eye height above the row floor</summary>
    public float EyeHeight { get; set; } = 1.15f;
    /// <summary>Width of the hall (m).</summary>
    public float Width { get; set; } = 13f;
    /// <summary>from the stage edge to the back wall</summary>
    public float Depth { get; set; } = 22f;
    /// <summary>Row specifications, front to back.</summary>
    public List<RowSpec> Rows { get; set; } = new();
    /// <summary>Named seats (e.g. "Regie").</summary>
    public List<Viewpoint> Viewpoints { get; set; } = new();
    /// <summary>e.g. the control position (grey seats)</summary>
    public List<SeatBlock> Reserved { get; set; } = new();

    /// <summary>All rows in order (first row = nearest the stage).</summary>
    public List<Row> Layout(float stageEdge)
    {
        var rows = new List<Row>();
        int index = 0;
        foreach (var spec in Rows)
            foreach (char name in spec.Names)
            {
                // Even numbers on house left (-X), odd numbers on house right; highest numbers in the middle.
                int even = spec.Even > 0 ? spec.Even : spec.Seats / 2;
                int odd = spec.Odd > 0 ? spec.Odd : spec.Seats - even;
                float shift = spec.Stagger != 0f && index % 2 == 1 ? spec.Stagger * SeatWidth : 0f;
                var row = new Row
                {
                    Name = name.ToString(),
                    Index = index,
                    Z = stageEdge + FirstRowDistance + index * RowPitch + RowPitch / 2f,
                    FloorY = -FirstRowDrop + index * Rake,
                };
                for (int i = 0; i < even; i++)
                    row.Seats.Add(new Seat(name.ToString(), 2 * (even - i), shift - (i + 0.5f) * SeatWidth));
                for (int i = 0; i < odd; i++)
                    row.Seats.Add(new Seat(name.ToString(), 2 * (odd - i) - 1, shift + (i + 0.5f) * SeatWidth));
                row.Seats.Sort((a, b) => a.X.CompareTo(b.X));
                rows.Add(row);
                index++;
            }
        return rows;
    }

    /// <summary>Eye position of a seat (stage coordinates), null if the seat doesn't exist.</summary>
    public float[] EyeOf(float stageEdge, string row, int number)
    {
        var r = Layout(stageEdge).FirstOrDefault(x => x.Name == row);
        var seat = r?.Seats.FirstOrDefault(s => s.Number == number);
        return seat == null ? null : new[] { seat.X, r.FloorY + EyeHeight, r.Z };
    }

    /// <summary>Eye position of a viewpoint: the average of its seats.</summary>
    public float[] EyeOf(float stageEdge, Viewpoint vp)
    {
        var eyes = vp.Seats.Select(n => EyeOf(stageEdge, vp.Row, n)).Where(e => e != null).ToList();
        if (eyes.Count == 0)
            return null;
        return new[] { eyes.Average(e => e[0]), eyes.Average(e => e[1]), eyes.Average(e => e[2]) };
    }

    /// <summary>True for a reserved seat (e.g. the control position).</summary>
    public bool IsReserved(string row, int number) =>
        Reserved.Any(b => b.Rows.Contains(row) && b.Seats.Contains(number));
}
