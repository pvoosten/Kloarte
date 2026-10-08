using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A seat: row, number and x position (stage coordinates).</summary>
/// <param name="Row">Row letter.</param>
/// <param name="Number">Seat number.</param>
/// <param name="X">Seat centre x (m).</param>
public sealed record Seat(string Row, int Number, float X);
