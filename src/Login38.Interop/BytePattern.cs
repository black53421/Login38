using System.Globalization;

namespace Login38.Interop;

/// <summary>
/// A byte signature with wildcards, for locating code in the game by shape rather than
/// by a fixed address.
/// </summary>
/// <remarks>
/// Written as IDA-style text: <c>"8B 45 ?? 89 45 EC"</c>, where <c>??</c> (or a bare
/// <c>?</c>) matches any byte. Wildcards cover the bytes that differ between client
/// builds — usually immediates and relative offsets.
/// </remarks>
public sealed class BytePattern
{
    private readonly byte[] _bytes;
    private readonly bool[] _isWildcard;

    private BytePattern(byte[] bytes, bool[] isWildcard)
    {
        _bytes = bytes;
        _isWildcard = isWildcard;

        // Matching starts from the first fixed byte, so a pattern that begins with
        // wildcards does not pay for them on every candidate position.
        FirstFixedIndex = Array.IndexOf(isWildcard, false);
    }

    /// <summary>Number of bytes the pattern spans, wildcards included.</summary>
    public int Length => _bytes.Length;

    /// <summary>Index of the first non-wildcard byte, or -1 if the pattern is all wildcards.</summary>
    internal int FirstFixedIndex { get; }

    internal byte FirstFixedByte => FirstFixedIndex >= 0 ? _bytes[FirstFixedIndex] : (byte)0;

    /// <summary>Parses IDA-style pattern text.</summary>
    /// <exception cref="FormatException">A token is neither a hex byte nor a wildcard.</exception>
    public static BytePattern Parse(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var tokens = pattern.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        var wildcards = new bool[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token is "?" or "??")
            {
                wildcards[i] = true;
                continue;
            }

            // Exactly two digits, always. Accepting a single digit would silently read
            // a typo like "8B 4 EC" as "8B 04 EC" and scan for the wrong signature —
            // which surfaces as a patch that mysteriously fails to find its target.
            if (token.Length != 2 ||
                !byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                throw new FormatException(
                    $"'{token}' is not a two-digit hex byte or a wildcard in pattern '{pattern}'.");
            }
        }

        return new BytePattern(bytes, wildcards);
    }

    /// <summary>Builds a pattern with no wildcards.</summary>
    public static BytePattern Exact(ReadOnlySpan<byte> bytes) =>
        new(bytes.ToArray(), new bool[bytes.Length]);

    /// <summary>Formats bytes as pattern text, so <c>Parse(Format(b))</c> round-trips.</summary>
    /// <remarks>
    /// Patterns are built by substituting values that are only known at runtime — a
    /// configured limit, a resolved address — into signature text. Producing that text in
    /// the same shape <see cref="Parse"/> reads keeps the substitution honest, and the
    /// same formatting makes mismatch messages line up with the pattern that failed.
    /// </remarks>
    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        // Two digits and a separator each, less the trailing separator.
        return string.Create((bytes.Length * 3) - 1, bytes.ToArray(), static (text, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var at = i * 3;
                if (i > 0)
                {
                    text[at - 1] = ' ';
                }

                source[i].TryFormat(text[at..], out _, "X2", CultureInfo.InvariantCulture);
            }
        });
    }

    /// <summary>Whether the pattern matches <paramref name="candidate"/> at its start.</summary>
    public bool Matches(ReadOnlySpan<byte> candidate)
    {
        if (candidate.Length < _bytes.Length)
        {
            return false;
        }

        for (var i = 0; i < _bytes.Length; i++)
        {
            if (!_isWildcard[i] && candidate[i] != _bytes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Index of the first match inside <paramref name="haystack"/>, or -1.</summary>
    /// <param name="haystack">Buffer to search.</param>
    public int IndexIn(ReadOnlySpan<byte> haystack)
    {
        if (_bytes.Length == 0 || haystack.Length < _bytes.Length)
        {
            return -1;
        }

        // All wildcards: every position matches, so the answer is trivially 0.
        if (FirstFixedIndex < 0)
        {
            return 0;
        }

        var last = haystack.Length - _bytes.Length;
        var searchFrom = 0;

        while (searchFrom <= last)
        {
            // Vectorised skip to the next occurrence of the first fixed byte.
            var window = haystack[(searchFrom + FirstFixedIndex)..];
            var hit = window.IndexOf(FirstFixedByte);
            if (hit < 0)
            {
                return -1;
            }

            var start = searchFrom + hit;
            if (start > last)
            {
                return -1;
            }

            if (Matches(haystack[start..]))
            {
                return start;
            }

            searchFrom = start + 1;
        }

        return -1;
    }
}
