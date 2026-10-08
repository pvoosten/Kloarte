namespace Kloarte.Model;

/// <summary>
/// Content fingerprints: 64-bit FNV-1a, the same in every run (unlike <c>string.GetHashCode</c>). Used to see whether a
/// document changed since it was loaded or saved.
/// </summary>
public static class Hashes
{
    /// <summary>FNV-1a offset basis.</summary>
    private const ulong Basis = 14695981039346656037;

    /// <summary>FNV-1a prime.</summary>
    private const ulong Prime = 1099511628211;

    /// <summary>The fingerprint of a text (UTF-16 code units).</summary>
    public static long Fnv(string text) => Add(unchecked((long)Basis), text);

    /// <summary>Adds a text to a fingerprint.</summary>
    public static long Add(long hash, string text)
    {
        ulong h = unchecked((ulong)hash);
        foreach (char c in text ?? "")
        {
            h = unchecked((h ^ (byte)c) * Prime);
            h = unchecked((h ^ (byte)(c >> 8)) * Prime);
        }
        return unchecked((long)h);
    }

    /// <summary>Adds a number to a fingerprint.</summary>
    public static long Add(long hash, long value)
    {
        ulong h = unchecked((ulong)hash);
        for (int i = 0; i < 8; i++)
            h = unchecked((h ^ (byte)(value >> (8 * i))) * Prime);
        return unchecked((long)h);
    }
}
