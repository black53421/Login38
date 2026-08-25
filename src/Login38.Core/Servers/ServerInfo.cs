using System.Buffers.Binary;
using System.Text;

namespace Login38.Core.Servers;

/// <summary>
/// One entry in the server list.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the client's <c>Server_Info</c> structure byte for byte: 213 bytes,
/// <c>#pragma pack(1)</c>, compiled with <c>UNICODE</c>. The layout cannot change —
/// it is shared with the game client through shared memory and with existing
/// <c>list.txt</c> files.
/// </para>
/// <code>
/// +0x00  64  wchar_t name[32]      UTF-16LE, NUL terminated
/// +0x40  32  char    ip[32]        ASCII, NUL terminated
/// +0x60   4  int     port
/// +0x64   1  bool    used
/// +0x65  16  BYTE    key[16]
/// +0x75   1  bool    encrypt
/// +0x76   1  bool    usehelper
/// +0x77   1  bool    usebd
/// +0x78  64  wchar_t bdfile[32]    UTF-16LE, NUL terminated
/// +0xB8   1  bool    randkey
/// +0xB9   4  ulong   rsa_e
/// +0xBD   4  ulong   rsa_d
/// +0xC1   4  ulong   rsa_n
/// +0xC5  16  BYTE    fix[16]       reserved, always zero
/// </code>
/// </remarks>
public sealed class ServerInfo
{
    /// <summary>Size of the serialized structure, in bytes.</summary>
    public const int SerializedSize = 213;

    /// <summary>Length of the per-server packet key.</summary>
    public const int KeyLength = 16;

    private const int NameOffset = 0x00;
    private const int NameBytes = 64;
    private const int IpOffset = 0x40;
    private const int IpBytes = 32;
    private const int PortOffset = 0x60;
    private const int UsedOffset = 0x64;
    private const int KeyOffset = 0x65;
    private const int EncryptOffset = 0x75;
    private const int UseHelperOffset = 0x76;
    private const int UseMorphFileOffset = 0x77;
    private const int MorphFileOffset = 0x78;
    private const int MorphFileBytes = 64;
    private const int RandomKeyOffset = 0xB8;
    private const int RsaEOffset = 0xB9;
    private const int RsaDOffset = 0xBD;
    private const int RsaNOffset = 0xC1;

    private byte[] _key = new byte[KeyLength];

    public ServerInfo()
    {
    }

    public ServerInfo(string name, string ipAddress, int port)
    {
        Name = name;
        IpAddress = ipAddress;
        Port = port;
        InUse = true;
    }

    /// <summary>Display name shown in the server list.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dotted-quad address or hostname.</summary>
    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    /// <summary>Whether this slot holds a real server. Original field: <c>used</c>.</summary>
    public bool InUse { get; set; }

    /// <summary>Per-server packet key. Always <see cref="KeyLength"/> bytes.</summary>
    public byte[] Key
    {
        get => _key;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfNotEqual(value.Length, KeyLength);
            _key = value;
        }
    }

    /// <summary>Whether packets to this server are encrypted.</summary>
    public bool Encrypt { get; set; }

    /// <summary>Original field: <c>usehelper</c>.</summary>
    public bool UseHelper { get; set; }

    /// <summary>Whether to load a morph <c>.pak</c>. Original field: <c>usebd</c>.</summary>
    public bool UseMorphFile { get; set; }

    /// <summary>Morph <c>.pak</c> file name. Original field: <c>bdfile</c>.</summary>
    public string MorphFileName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the server issues a fresh RSA key per session rather than using the
    /// fixed <see cref="RsaE"/>/<see cref="RsaD"/>/<see cref="RsaN"/> triple.
    /// Original field: <c>randkey</c>.
    /// </summary>
    public bool RandomKey { get; set; }

    /// <summary>Public exponent of the login handshake key.</summary>
    public uint RsaE { get; set; }

    /// <summary>Private exponent of the login handshake key.</summary>
    public uint RsaD { get; set; }

    /// <summary>Modulus of the login handshake key.</summary>
    public uint RsaN { get; set; }

    /// <summary>Serializes into a fresh <see cref="SerializedSize"/>-byte buffer.</summary>
    public byte[] ToBytes()
    {
        var buffer = new byte[SerializedSize];
        WriteTo(buffer);
        return buffer;
    }

    /// <summary>Serializes into <paramref name="destination"/>, which is cleared first.</summary>
    public void WriteTo(Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, SerializedSize);

        // Clearing up front is what leaves the reserved fix[16] tail zeroed, and what
        // NUL-terminates both wide strings without writing terminators explicitly.
        destination[..SerializedSize].Clear();

        WriteWideString(destination.Slice(NameOffset, NameBytes), Name);
        WriteAsciiString(destination.Slice(IpOffset, IpBytes), IpAddress);

        BinaryPrimitives.WriteInt32LittleEndian(destination[PortOffset..], Port);
        destination[UsedOffset] = InUse ? (byte)1 : (byte)0;
        _key.CopyTo(destination[KeyOffset..]);
        destination[EncryptOffset] = Encrypt ? (byte)1 : (byte)0;
        destination[UseHelperOffset] = UseHelper ? (byte)1 : (byte)0;
        destination[UseMorphFileOffset] = UseMorphFile ? (byte)1 : (byte)0;

        WriteWideString(destination.Slice(MorphFileOffset, MorphFileBytes), MorphFileName);

        destination[RandomKeyOffset] = RandomKey ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[RsaEOffset..], RsaE);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[RsaDOffset..], RsaD);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[RsaNOffset..], RsaN);
    }

    /// <summary>Deserializes from a <see cref="SerializedSize"/>-byte buffer.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The buffer is too short.</exception>
    public static ServerInfo Parse(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, SerializedSize);

        var info = new ServerInfo
        {
            Name = ReadWideString(source.Slice(NameOffset, NameBytes)),
            IpAddress = ReadAsciiString(source.Slice(IpOffset, IpBytes)),
            Port = BinaryPrimitives.ReadInt32LittleEndian(source[PortOffset..]),
            InUse = source[UsedOffset] != 0,
            Key = source.Slice(KeyOffset, KeyLength).ToArray(),
            Encrypt = source[EncryptOffset] != 0,
            UseHelper = source[UseHelperOffset] != 0,
            UseMorphFile = source[UseMorphFileOffset] != 0,
            MorphFileName = ReadWideString(source.Slice(MorphFileOffset, MorphFileBytes)),
            RandomKey = source[RandomKeyOffset] != 0,
            RsaE = BinaryPrimitives.ReadUInt32LittleEndian(source[RsaEOffset..]),
            RsaD = BinaryPrimitives.ReadUInt32LittleEndian(source[RsaDOffset..]),
            RsaN = BinaryPrimitives.ReadUInt32LittleEndian(source[RsaNOffset..]),
        };

        return info;
    }

    /// <summary>
    /// Writes UTF-16LE, truncating so at least one unit is left for the terminator.
    /// The destination is assumed to be zeroed.
    /// </summary>
    private static void WriteWideString(Span<byte> destination, string value)
    {
        var maxUnits = destination.Length / sizeof(char) - 1;
        var units = Math.Min(value.Length, maxUnits);
        for (var i = 0; i < units; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * sizeof(char))..], value[i]);
        }
    }

    private static void WriteAsciiString(Span<byte> destination, string value)
    {
        var maxBytes = destination.Length - 1;
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, maxBytes)).CopyTo(destination);
    }

    private static string ReadWideString(ReadOnlySpan<byte> source)
    {
        for (var offset = 0; offset + sizeof(char) <= source.Length; offset += sizeof(char))
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]) == 0)
            {
                return Encoding.Unicode.GetString(source[..offset]);
            }
        }

        return Encoding.Unicode.GetString(source);
    }

    private static string ReadAsciiString(ReadOnlySpan<byte> source)
    {
        var end = source.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? source : source[..end]);
    }
}
