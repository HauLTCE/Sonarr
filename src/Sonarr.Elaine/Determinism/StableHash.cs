using System.Text;

namespace Sonarr.Elaine.Determinism;

/// <summary>
/// FNV-1a over UTF-8. Used wherever a string has to become a seed.
/// </summary>
/// <remarks>
/// <c>string.GetHashCode()</c> is randomized per process, so it can never appear on a
/// determinism path: two runs of the same turn would draw different lines. This is the
/// stable replacement — same text, same number, forever.
/// </remarks>
public static class StableHash
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>64-bit FNV-1a of <paramref name="text"/> (UTF-8 bytes).</summary>
    public static ulong Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ulong hash = Offset;
        int max = Encoding.UTF8.GetMaxByteCount(text.Length);
        byte[]? rented = max > 256 ? new byte[max] : null;
        Span<byte> buffer = rented ?? stackalloc byte[256];
        int written = Encoding.UTF8.GetBytes(text, buffer);
        for (int i = 0; i < written; i++)
        {
            hash ^= buffer[i];
            hash *= Prime;
        }

        return hash;
    }
}
