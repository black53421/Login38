using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Login38.Interop;

/// <summary>
/// A virtual address inside the game process.
/// </summary>
/// <remarks>
/// <para>
/// The reference implementation passed bare <c>u32</c> for addresses, sizes, offsets,
/// module bases and RVAs alike. Those are not interchangeable, and confusing an RVA
/// with an absolute address writes a jump to the wrong place — which corrupts the game
/// rather than failing loudly. This type makes the distinction visible at every call
/// site while still being a single <see cref="uint"/> at runtime.
/// </para>
/// <para>
/// Always 32 bits: the game is a 32-bit process, and this launcher must be built x86 to
/// match it.
/// </para>
/// </remarks>
public readonly record struct GameAddress(uint Value) :
    IComparable<GameAddress>,
    IParsable<GameAddress>
{
    public static GameAddress Zero => default;

    public bool IsNull => Value == 0;

    /// <summary>The address as a pointer, for the Win32 calls that take one.</summary>
    public nint ToPointer() => (nint)Value;

    /// <summary>Adds a byte offset, wrapping like the pointer arithmetic it stands in for.</summary>
    public static GameAddress operator +(GameAddress address, int offset) =>
        new(unchecked((uint)(address.Value + offset)));

    /// <inheritdoc cref="op_Addition(GameAddress,int)"/>
    public static GameAddress operator +(GameAddress address, uint offset) =>
        new(unchecked(address.Value + offset));

    public static GameAddress operator -(GameAddress address, int offset) =>
        new(unchecked((uint)(address.Value - offset)));

    /// <summary>The signed distance between two addresses, as used for relative jumps.</summary>
    public static int operator -(GameAddress left, GameAddress right) =>
        unchecked((int)(left.Value - right.Value));

    public static bool operator <(GameAddress left, GameAddress right) => left.Value < right.Value;

    public static bool operator >(GameAddress left, GameAddress right) => left.Value > right.Value;

    public static bool operator <=(GameAddress left, GameAddress right) => left.Value <= right.Value;

    public static bool operator >=(GameAddress left, GameAddress right) => left.Value >= right.Value;

    /// <summary>The lower of two addresses, for clamping a range to a region.</summary>
    public static GameAddress Min(GameAddress left, GameAddress right) =>
        left.Value <= right.Value ? left : right;

    /// <inheritdoc cref="Min"/>
    public static GameAddress Max(GameAddress left, GameAddress right) =>
        left.Value >= right.Value ? left : right;

    public int CompareTo(GameAddress other) => Value.CompareTo(other.Value);

    /// <summary>
    /// Explicit in both directions. An implicit conversion would defeat the point:
    /// any <see cref="uint"/> in scope would silently become an address.
    /// </summary>
    public static explicit operator GameAddress(uint value) => new(value);

    public static explicit operator uint(GameAddress address) => address.Value;

    /// <summary>Formats as <c>0x004B3EE0</c>, matching how these appear in the notes.</summary>
    public override string ToString() => $"0x{Value:X8}";

    /// <summary>
    /// Parses hex, with or without a <c>0x</c> prefix.
    /// </summary>
    /// <remarks>
    /// Named rather than exposed as <c>Parse</c> because addresses are never
    /// culture-sensitive; a public <c>Parse(string, IFormatProvider)</c> would make
    /// every call site look like it had a culture decision to make, and trip the
    /// globalization analyzer for no reason. <see cref="IParsable{TSelf}"/> is
    /// implemented explicitly for generic code that needs it.
    /// </remarks>
    /// <exception cref="FormatException">The text is not a valid 32-bit hex address.</exception>
    public static GameAddress FromHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParseHex(text, out var result)
            ? result
            : throw new FormatException($"'{text}' is not a valid address.");
    }

    /// <inheritdoc cref="FromHex"/>
    public static bool TryParseHex([NotNullWhen(true)] string? text, out GameAddress result)
    {
        result = default;
        if (text is null)
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        if (!uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        result = new GameAddress(value);
        return true;
    }

    static GameAddress IParsable<GameAddress>.Parse(string s, IFormatProvider? provider) => FromHex(s);

    static bool IParsable<GameAddress>.TryParse(string? s, IFormatProvider? provider, out GameAddress result) =>
        TryParseHex(s, out result);
}
