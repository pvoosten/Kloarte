namespace Kloarte.Model;

/// <summary>Head names as the console keeps them: short, without quotes.</summary>
public static class HeadNames
{
    /// <summary>Longest head name the console keeps (the longest in all 64 shows of the show folder).</summary>
    public const int MaxLength = 15;

    /// <summary>A name as the console can keep it: trimmed, without double quotes, cut to <see cref="MaxLength"/>.</summary>
    public static string Clean(string name)
    {
        string clean = (name ?? "").Trim().Replace("\"", "");
        return clean.Length > MaxLength ? clean[..MaxLength].TrimEnd() : clean;
    }
}
