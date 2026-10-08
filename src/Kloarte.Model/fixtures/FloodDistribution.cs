using System;

namespace Kloarte.Model;

/// <summary>
/// Light distribution of an asymmetric cyc flood (ADB ACP1001 style): very wide horizontally and throwing most of its
/// light towards the far end of the cloth, so a cloth lit from close by is lit evenly. An even wash on a flat surface
/// needs I ~ 1 / cos^3(angle) (distance and incidence); the reflector approximates that up to a limit.
/// Angles in degrees: h = horizontal (+ = right), v = towards the far end of the cloth (+) or towards the near edge (-).
/// Values are relative (max 1). No Godot dependencies.
/// </summary>
public static class FloodDistribution
{
    /// <summary>A bit less than the ideal 3 (distance and incidence): real floods are a little brighter near by.</summary>
    private const double Exponent = 2.8;
    private const double HorizontalLimit = 70, HorizontalCut = 84;
    private const double FarLimit = 76, FarCut = 84.5;
    /// <summary>little light towards the near edge (behind the top of the cloth)</summary>
    private const double NearCut = -30;

    /// <summary>Relative intensity (0..1) at horizontal angle h and angle v towards the far end (degrees).</summary>
    public static double Relative(double h, double v)
    {
        double hr = Math.Abs(h) * Math.PI / 180.0, vr = Math.Abs(v) * Math.PI / 180.0;
        double horizontal = Math.Pow(Math.Cos(Math.Min(hr, HorizontalLimit * Math.PI / 180.0)), -Exponent)
                            * (1.0 - SmoothStep(HorizontalLimit, HorizontalCut, Math.Abs(h)));
        double vertical = v >= 0
            ? Math.Pow(Math.Cos(Math.Min(vr, FarLimit * Math.PI / 180.0)), -Exponent) * (1.0 - SmoothStep(FarLimit, FarCut, v))
            : SmoothStep(NearCut, 0, v);
        return horizontal * vertical / Max;
    }

    private static readonly double Max =
        Math.Pow(Math.Cos(HorizontalLimit * Math.PI / 180.0), -Exponent) * Math.Pow(Math.Cos(FarLimit * Math.PI / 180.0), -Exponent);

    /// <summary>Integral of Relative over the sphere (sr): peak candela = lumens / this.</summary>
    public static double EffectiveSolidAngle()
    {
        // Integrate over directions (x, y, -1) on the projection plane: dOmega = dx dy / (1 + x^2 + y^2)^1.5.
        const int n = 400;
        // tan(85°) ~ 11.4
        const double extent = 12.0;
        double step = 2 * extent / n, sum = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double x = -extent + (i + 0.5) * step, y = -extent + (j + 0.5) * step;
                double h = Math.Atan(x) * 180 / Math.PI, v = Math.Atan(y) * 180 / Math.PI;
                sum += Relative(h, v) * step * step / Math.Pow(1 + x * x + y * y, 1.5);
            }
        return sum;
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }
}
