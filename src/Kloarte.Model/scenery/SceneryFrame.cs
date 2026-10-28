namespace Kloarte.Model;

/// <summary>A set piece at one moment of the timeline: on stage or not, and how far a curtain is open (0..100 %).</summary>
public readonly record struct SceneryFrame(bool Visible, float Open);
