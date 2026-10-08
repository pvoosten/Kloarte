using System;

namespace Kloarte.Model;

/// <summary>
/// Light strength of a fixture, physically based. No Godot dependencies.
///
/// A lamp puts a fixed luminous flux (lumens) through the lens. Spread over the field (solid angle Omega of the cone)
/// that gives the peak intensity in candela: zooming a profile / PC / fresnel narrower makes it brighter.
/// The field is not flat: the mask falls off to `falloff` at the edge as mix(1, falloff, r^2), whose average over the
/// disc is (1 + falloff) / 2, so I_peak = flux / (Omega * (1 + falloff) / 2).
/// Illuminance then falls with the square of the distance (the SpotLight3D uses inverse-square attenuation).
/// </summary>
public static class Photometrics
{
    /// <summary>Solid angle (sr) of a cone with the given full angle in degrees.</summary>
    public static double SolidAngle(double fieldDegrees) =>
        2.0 * Math.PI * (1.0 - Math.Cos(fieldDegrees * Math.PI / 360.0));

    /// <summary>Peak intensity (cd) of a flux spread over a field angle with the given edge falloff.</summary>
    public static double PeakCandela(double lumens, double fieldDegrees, double falloff) =>
        lumens / (SolidAngle(fieldDegrees) * (1.0 + Math.Clamp(falloff, 0.0, 1.0)) / 2.0);

    /// <summary>Illuminance (lux) on a surface at `distance` metres, perpendicular to the beam axis.</summary>
    public static double Lux(double candela, double distance) => candela / (distance * distance);
}
