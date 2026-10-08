using System;
using System.Globalization;

namespace Kloarte.Model;

/// <summary>
/// Shirt colours of the actors (user: a colour per actor, the name printed on chest and back): an actor's own
/// <see cref="ActorDef.ShirtColor"/>, else a colour of a palette of clearly different colours by the actor's place in
/// the blocking. Colours as "rrggbb".
/// </summary>
public static class ShirtColors
{
    /// <summary>The palette: ten colours that are easy to tell apart on stage.</summary>
    public static readonly string[] Palette =
    {
        "c0392b", "2471a3", "229954", "7d3c98", "ca6f1e", "17a589", "d4ac0d", "34495e", "c2185b", "7f8c8d",
    };

    /// <summary>The shirt colour of an actor: its own, else the palette's colour for its place in the blocking.</summary>
    public static string Of(Blocking blocking, ActorDef actor)
    {
        if (Parse(actor.ShirtColor) is not null)
            return actor.ShirtColor.TrimStart('#').ToLowerInvariant();
        int index = Math.Max(0, blocking.Actors.IndexOf(actor));
        return Palette[index % Palette.Length];
    }

    /// <summary>Red, green, blue (0..1) of "rrggbb" (or "#rrggbb"); null when it is not such a colour.</summary>
    public static (float R, float G, float B)? Parse(string hex)
    {
        string h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return null;
        return (((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
    }

    /// <summary>True when black letters read better on the colour than white ones (a light shirt).</summary>
    public static bool DarkLetters(string hex) =>
        Parse(hex) is { } c && 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B > 0.55f;
}
