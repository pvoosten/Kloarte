using System;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Where a lamp can move: a lamp hanging on a truss or track slides along it (positions along its line, within its
/// length; the drop below it and any offset stay), a lamp on a floor stand moves up and down it, a lamp without a support
/// moves freely. An axis the support's direction doesn't have is fixed: its field is disabled, and a value for it
/// is refused. Rig coordinates (the same axes as the stage's, so also valid for stage positions). No Godot dependencies.
/// </summary>
public sealed class LampMotion
{
    /// <summary>Smallest share of the support's direction an axis needs to count as a direction of motion.</summary>
    private const float AxisShare = 0.01f;

    /// <summary>The lamp's position (rig coordinates) the rule was made for.</summary>
    private readonly Vector3 _lamp;

    /// <summary>Unit direction of the support; unused without a support.</summary>
    private readonly Vector3 _direction;

    /// <summary>Range of the lamp's travel along the support's direction, from where it is now.</summary>
    private readonly float _backwards, _forwards;

    /// <summary>A lamp that moves freely: no support, or a support that is not a pipe.</summary>
    internal LampMotion(Vector3 lamp)
    {
        _lamp = lamp;
        _backwards = _forwards = float.PositiveInfinity;
    }

    /// <summary>A lamp on a truss, track or stand.</summary>
    internal LampMotion(RigSupport support, Vector3 lamp)
    {
        Support = support;
        _lamp = lamp;
        _direction = support.Direction;
        float at = Vector3.Dot(lamp - support.Start, _direction);
        // A lamp on a stand may go on to a little above its top, where Rig.Snap puts it (Rig.StandMount).
        float mount = support.IsStand ? Rig.StandMount : 0f;
        bool endIsTop = support.End.Y >= support.Start.Y;
        float low = endIsTop ? 0f : -mount, high = support.Length + (endIsTop ? mount : 0f);
        _backwards = Math.Max(0f, at - low);
        _forwards = Math.Max(0f, high - at);
    }

    /// <summary>The truss, track or stand the lamp hangs on; null when it moves freely.</summary>
    public RigSupport Support { get; }

    /// <summary>True when the lamp hangs on a truss or track (not a stand, not free).</summary>
    public bool OnTruss => Support is { IsStand: false };

    /// <summary>True when the lamp can move in this axis' direction.</summary>
    public bool IsFree(MotionAxis axis) => Support == null || Math.Abs(Component(_direction, axis)) > AxisShare;

    /// <summary>
    /// Why the lamp can't move in this axis' direction, for the tooltip of its disabled field; "" when it can.
    /// </summary>
    public string WhyFixed(MotionAxis axis)
    {
        if (IsFree(axis))
            return "";
        if (Support.IsStand)
            return $"Stands on {Support.Name}: only the height can change. Move the stand to move the lamp";
        return axis == MotionAxis.Y
            ? $"Follows {Support.Name}'s height; change that in Truss heights"
            : $"Moves along {Support.Name} only";
    }

    /// <summary>
    /// The lamp's position after setting one axis to `value`. A lamp on a support slides along it until that axis has
    /// the value, so the other free axes of a diagonal truss follow; it stops at the ends (`stopped` = true). Null
    /// when the axis is fixed.
    /// </summary>
    public Vector3? Move(MotionAxis axis, float value, out bool stopped)
    {
        stopped = false;
        if (!IsFree(axis))
            return null;
        if (Support == null)
            return With(_lamp, axis, value);
        float share = Component(_direction, axis);
        float travel = (value - Component(_lamp, axis)) / share;
        float limited = Math.Clamp(travel, -_backwards, _forwards);
        stopped = Math.Abs(limited - travel) > 1e-4f;
        return _lamp + _direction * limited;
    }

    /// <summary>The component of a vector on an axis.</summary>
    private static float Component(Vector3 v, MotionAxis axis) => axis switch
    {
        MotionAxis.X => v.X,
        MotionAxis.Y => v.Y,
        _ => v.Z,
    };

    /// <summary>A vector with one component set.</summary>
    private static Vector3 With(Vector3 v, MotionAxis axis, float value) => axis switch
    {
        MotionAxis.X => v with { X = value },
        MotionAxis.Y => v with { Y = value },
        _ => v with { Z = value },
    };
}
