using System;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// A lamp's mechanics as its <see cref="LampFile"/> and back. The file holds only what differs from the defaults of the
/// lamp's catalogue model (<see cref="FixtureMechanics.DefaultFor"/>), rounded for reading (user: metres 3 decimals,
/// degrees 0.1); a value equal to the default after rounding is left out. Shutters and barndoors that are fully open
/// are left out (the same as none); a missing value reads as the default.
/// </summary>
public static class MechanicsDiff
{
    /// <summary>Decimals of degrees, of metres and of the other fractions (0..1 values, depths).</summary>
    private const int Degrees = 1, Metres = 3, Fraction = 3;

    /// <summary>The defaults a lamp's file is compared with: those of its catalogue model (or kind).</summary>
    public static FixtureMechanics DefaultsOf(string model, string kind, FixtureCatalog catalog)
    {
        var fixtureModel = catalog?.Resolve(model) ?? catalog?.Resolve(kind);
        return FixtureMechanics.DefaultFor(fixtureModel, model ?? kind ?? FixtureKind.Pc);
    }

    /// <summary>The file of a lamp (rig item `rigItem`) with these mechanics.</summary>
    public static LampFile ToFile(int rigItem, FixtureMechanics m, FixtureCatalog catalog)
    {
        var d = DefaultsOf(m.Model, m.Kind, catalog);
        bool known = m.Model != null && catalog?.Find(m.Model) != null;
        return new LampFile
        {
            RigItem = rigItem,
            Model = m.Model,
            Kind = !known || m.Kind != d.Kind ? m.Kind : null,
            FieldAngle = Diff(m.FieldAngle, d.FieldAngle, Degrees),
            Softness = Diff(m.Softness, d.Softness, Fraction),
            FieldFalloff = Diff(m.FieldFalloff, d.FieldFalloff, Fraction),
            Energy = Diff(m.Energy, d.Energy, Degrees),
            Output = Diff(m.Output, d.Output, Fraction),
            Color = string.Equals(m.Color, d.Color, StringComparison.OrdinalIgnoreCase) ? null : m.Color?.ToLowerInvariant(),
            Shutters = ShuttersFile(m.Shutters),
            Iris = Diff(m.Iris, d.Iris, Fraction),
            Gobo = string.IsNullOrEmpty(m.Gobo?.Path) ? null : new LampFileGobo
            {
                Path = m.Gobo.Path,
                Rotation = Diff(m.Gobo.Rotation, 0, Degrees),
                Size = Diff(m.Gobo.Size, 1, Fraction),
            },
            Barndoors = BarndoorsFile(m.Barndoors),
            Lamp = Same(m.Lamp, d.Lamp) ? null : m.Lamp,
            OvalRotation = Diff(m.OvalRotation, 0, Degrees),
            Mounting = Same(m.Mounting, d.Mounting) ? null : m.Mounting,
            FocusPoint = m.FocusPoint is { Length: 3 } p ? p.Select(v => Math.Round((double)v, Metres, MidpointRounding.AwayFromZero)).ToArray() : null,
        };
    }

    /// <summary>The mechanics a lamp file describes (missing values = the model's defaults).</summary>
    public static FixtureMechanics FromFile(LampFile f, FixtureCatalog catalog)
    {
        var m = DefaultsOf(f.Model, f.Kind, catalog);
        m.Model = f.Model ?? m.Model;
        m.Kind = f.Kind ?? m.Kind;
        m.FieldAngle = (float)(f.FieldAngle ?? m.FieldAngle);
        m.Softness = (float)(f.Softness ?? m.Softness);
        m.FieldFalloff = (float)(f.FieldFalloff ?? m.FieldFalloff);
        m.Energy = (float)(f.Energy ?? m.Energy);
        m.Output = (float)(f.Output ?? m.Output);
        m.Color = f.Color ?? m.Color;
        if (f.Shutters is { } s)
            m.Shutters = new Shutters
            {
                Top = Blade(s.Top), Right = Blade(s.Right), Bottom = Blade(s.Bottom), Left = Blade(s.Left),
                Rotation = (float)(s.Rotation ?? 0),
            };
        m.Iris = (float)(f.Iris ?? m.Iris);
        if (f.Gobo is { Path: { Length: > 0 } path } g)
            m.Gobo = new Gobo { Path = path, Rotation = (float)(g.Rotation ?? 0), Size = (float)(g.Size ?? 1) };
        if (f.Barndoors is { } b)
            m.Barndoors = new Barndoors
            {
                Top = (float)(b.Top ?? 0), Right = (float)(b.Right ?? 0), Bottom = (float)(b.Bottom ?? 0),
                Left = (float)(b.Left ?? 0), Rotation = (float)(b.Rotation ?? 0),
            };
        m.Lamp = f.Lamp ?? m.Lamp;
        m.OvalRotation = (float)(f.OvalRotation ?? m.OvalRotation);
        m.Mounting = f.Mounting ?? m.Mounting;
        if (f.FocusPoint is { Length: 3 } p)
            m.FocusPoint = p.Select(v => (float)v).ToArray();
        return m;
    }

    /// <summary>The rounded value, or null when it rounds to the same as the default.</summary>
    private static double? Diff(float value, float defaultValue, int decimals)
    {
        var v = FileNumbers.Round(value, decimals);
        return v == FileNumbers.Round(defaultValue, decimals) ? null : v;
    }

    /// <summary>Two optional texts are the same (null = "").</summary>
    private static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>The shutters' file part; null when every blade is out and the assembly isn't turned.</summary>
    private static LampFileShutters ShuttersFile(Shutters s)
    {
        if (s == null)
            return null;
        var file = new LampFileShutters
        {
            Top = BladeFile(s.Top), Right = BladeFile(s.Right), Bottom = BladeFile(s.Bottom), Left = BladeFile(s.Left),
            Rotation = Diff(s.Rotation, 0, Degrees),
        };
        return file.Top == null && file.Right == null && file.Bottom == null && file.Left == null && file.Rotation == null
            ? null : file;
    }

    /// <summary>A blade's file part; null when it is out and not tilted.</summary>
    private static LampFileBlade BladeFile(Shutters.Blade b)
    {
        if (b == null)
            return null;
        var file = new LampFileBlade { Depth = Diff(b.Depth, 0, Fraction), Angle = Diff(b.Angle, 0, Degrees) };
        return file.Depth == null && file.Angle == null ? null : file;
    }

    /// <summary>A blade from its file part (missing = out).</summary>
    private static Shutters.Blade Blade(LampFileBlade b) =>
        new() { Depth = (float)(b?.Depth ?? 0), Angle = (float)(b?.Angle ?? 0) };

    /// <summary>The barndoors' file part; null when every flap is open and the ring isn't turned.</summary>
    private static LampFileBarndoors BarndoorsFile(Barndoors b)
    {
        if (b == null)
            return null;
        var file = new LampFileBarndoors
        {
            Top = Diff(b.Top, 0, Fraction), Right = Diff(b.Right, 0, Fraction), Bottom = Diff(b.Bottom, 0, Fraction),
            Left = Diff(b.Left, 0, Fraction), Rotation = Diff(b.Rotation, 0, Degrees),
        };
        return file.Top == null && file.Right == null && file.Bottom == null && file.Left == null && file.Rotation == null
            ? null : file;
    }
}
