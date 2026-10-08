using System.Numerics;

namespace Kloarte.Model;

/// <summary>Where an actor is and what it does at one moment of the timeline. No Godot dependencies.</summary>
public readonly record struct ActorFrame(bool Visible, string Pose, Vector3 Position, float Facing);
