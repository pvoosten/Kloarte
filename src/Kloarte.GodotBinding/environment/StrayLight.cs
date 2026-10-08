using System.Collections.Generic;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Stray light: in a real theatre the rest of the room never goes fully dark once something is lit (lens flare and
/// spill, haze scattering, bounce from the floor, set and people). Godot's GI (SDFGI) handles the bounce off the
/// geometry; this adds the remaining room-wide glow as ambient light that follows the total amount of light on stage
/// (sum of the luminous flux x colour of all fixtures), in the mixed colour of that light.
/// </summary>
public partial class StrayLight : Node
{
    /// <summary>0 = off, 1 = a lot. Tune by eye against the real venue.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float Amount = 0.5f;
    /// <summary>The WorldEnvironment whose ambient light is set.</summary>
    [Export] public NodePath WorldEnvironment = "../WorldEnvironment";

    /// <summary>ambient energy per lumen leaving the fixtures</summary>
    private const float Scale = 6e-6f;
    /// <summary>seconds; fixtures are regenerated when a show is (re)loaded</summary>
    private const float RescanInterval = 1.0f;

    private Environment _env;
    private float _baseEnergy;
    private Color _baseColor;
    private readonly List<ProfileFixture> _fixtures = new();
    private double _sinceScan = RescanInterval;

    /// <inheritdoc />
    public override void _Ready()
    {
        _env = GetNodeOrNull<WorldEnvironment>(WorldEnvironment)?.Environment;
        if (_env == null)
            return;
        _baseEnergy = _env.AmbientLightEnergy;
        _baseColor = _env.AmbientLightColor;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_env == null)
            return;
        if ((_sinceScan += delta) >= RescanInterval)
        {
            _sinceScan = 0;
            _fixtures.Clear();
            Collect(GetTree().Root);
        }

        var sum = new Color(0, 0, 0);
        foreach (var f in _fixtures)
        {
            if (!IsInstanceValid(f) || f.Intensity <= 0f)
                continue;
            float power = f.Flux;
            var c = f.LampColor * f.GelColor;
            sum += new Color(c.R * power, c.G * power, c.B * power);
        }

        float total = Mathf.Max(sum.R, Mathf.Max(sum.G, sum.B));
        float stray = total * Scale * Amount;
        // Mix the base ambient (energy _baseEnergy) with the stray light (energy stray, normalised colour).
        var strayColor = total > 0f ? new Color(sum.R / total, sum.G / total, sum.B / total) : Colors.Black;
        float energy = _baseEnergy + stray;
        _env.AmbientLightEnergy = energy;
        _env.AmbientLightColor = energy > 0f
            ? (_baseColor * _baseEnergy + strayColor * stray) / energy
            : _baseColor;
    }

    private void Collect(Node node)
    {
        if (node is ProfileFixture f)
            _fixtures.Add(f);
        foreach (Node child in node.GetChildren())
            Collect(child);
    }
}
