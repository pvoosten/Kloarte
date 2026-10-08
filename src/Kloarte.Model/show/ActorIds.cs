using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kloarte.Model;

/// <summary>
/// Ids of new actors: a slug of the name ("Jean-François Müller" -&gt; "jean-francois-muller"), unique among the actors
/// (a second "Anna" becomes "anna-2"), so the blocking files read as <c>anna:</c> instead of a random id. A new id is made
/// when the actor is created; renaming the actor keeps its id (the steps refer to it).
/// </summary>
public static class ActorIds
{
    /// <summary>The id of an actor with no usable name (no letters or digits).</summary>
    public const string Fallback = "actor";

    /// <summary>Longest slug (a long name is cut at a dash where it can).</summary>
    private const int MaxLength = 32;

    /// <summary>
    /// A name as a slug: lower case ASCII letters and digits, accents dropped, anything else a single dash, no dash at
    /// the ends; "" when nothing is left.
    /// </summary>
    public static string Slug(string name)
    {
        var text = new StringBuilder();
        bool dash = false;
        foreach (char c in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            char lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (dash && text.Length > 0)
                    text.Append('-');
                text.Append(lower);
                dash = false;
            }
            else
                dash = true;
        }
        string slug = text.ToString();
        if (slug.Length > MaxLength)
        {
            int cut = slug.LastIndexOf('-', MaxLength - 1);
            slug = slug[..(cut > 0 ? cut : MaxLength)];
        }
        return slug;
    }

    /// <summary>The slug of a name that no actor in `taken` has: "anna", "anna-2", "anna-3"; <see cref="Fallback"/> for no name.</summary>
    public static string For(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken ?? Enumerable.Empty<string>(), System.StringComparer.OrdinalIgnoreCase);
        string slug = Slug(name);
        if (slug.Length == 0)
            slug = Fallback;
        string id = slug;
        for (int n = 2; used.Contains(id); n++)
            id = $"{slug}-{n}";
        return id;
    }
}
