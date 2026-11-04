using System;

namespace Kloarte.Model;

/// <summary>
/// Light distribution of an asymmetric cyc flood (ADB ACP1001 style), hung with its open front towards the cloth:
/// wide and soft horizontally (brightest straight ahead, gone by 80°), and throwing more and more light towards the far
/// end of the cloth (up to 80°), so a row of floods 1.2..2 m in front of the cloth lights it evenly, a little brighter
/// near them. Fitted to that: across within ~10 %, top to bottom within ~20 % (MagicQ shows no distribution; the
/// ACP1001's photometry is not published).
/// Angles in degrees from the level direction towards the cloth: h = horizontal (+ = right), v = towards the far end of
/// the cloth (+) or away from it (-). Values are relative (max 1). No Godot dependencies.
/// </summary>
public static class FloodDistribution
{
    /// <summary>Horizontal: cos(h) to this power, faded out between the two angles.</summary>
    private const double HorizontalPower = 1.0, HorizontalFade = 65, HorizontalCut = 80;

    /// <summary>Towards the far end: 1 / cos(v) to this power up to FarLimit, faded out by FarCut.</summary>
    private const double FarPower = 2.5, FarLimit = 80, FarCut = 86;

    /// <summary>Little light away from the far end (behind the top of the cloth).</summary>
    private const double NearCut = -30;

    /// <summary>Relative intensity (0..1) at horizontal angle h and angle v towards the far end (degrees).</summary>
    public static double Relative(double h, double v)
    {
        double horizontal = Math.Pow(Math.Cos(Math.Min(Math.Abs(h), 89.0) * Math.PI / 180.0), HorizontalPower)
                            * (1.0 - SmoothStep(HorizontalFade, HorizontalCut, Math.Abs(h)));
        double vertical = v >= 0
            ? Math.Pow(Math.Cos(Math.Min(v, FarLimit) * Math.PI / 180.0), -FarPower) * (1.0 - SmoothStep(FarLimit, FarCut, v))
            : SmoothStep(NearCut, 0, v);
        return horizontal * vertical / Max;
    }

    /// <summary>The largest value of the unscaled distribution (straight ahead horizontally, at the far limit).</summary>
    private static readonly double Max = Math.Pow(Math.Cos(FarLimit * Math.PI / 180.0), -FarPower);

    /// <summary>Integral of Relative over the sphere (sr): peak candela = lumens / this.</summary>
    public static double EffectiveSolidAngle()
    {
        // Integrate over directions (x, y, -1) on the projection plane: dOmega = dx dy / (1 + x^2 + y^2)^1.5.
        const int n = 600;
        // tan(86°) ~ 14.3
        const double extent = 15.0;
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

    /// <summary>Hermite step from 0 at edge0 to 1 at edge1.</summary>
    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }
}
