using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>One argument to the client's packet function.</summary>
/// <remarks>
/// The function is variadic C, so every argument occupies one stack slot whatever the
/// format string says its width is. What differs is where the value comes from: an
/// immediate, a global the client keeps, or a string that has to live somewhere with an
/// address of its own.
/// </remarks>
internal readonly record struct PacketArgument
{
    private PacketArgument(uint value, GameAddress? indirect, byte[]? text)
    {
        Value = value;
        Indirect = indirect;
        Text = text;
    }

    /// <summary>The immediate, when it is one.</summary>
    public uint Value { get; }

    /// <summary>A global to read the value out of, when it is one.</summary>
    public GameAddress? Indirect { get; }

    /// <summary>The bytes to embed and pass the address of, when it is one.</summary>
    public byte[]? Text { get; }

    /// <summary>A number known when the call is built.</summary>
    public static PacketArgument Number(uint value) => new(value, null, null);

    /// <summary>Something the client is keeping, read when the call runs.</summary>
    public static PacketArgument From(GameAddress address) => new(0, address, null);

    /// <summary>A NUL-terminated string, carried in the cave with the code.</summary>
    public static PacketArgument Text8(ReadOnlySpan<byte> bytes) => new(0, null, bytes.ToArray());
}

/// <summary>
/// Builds a call to the client's packet function that can be run anywhere.
/// </summary>
/// <remarks>
/// <para>
/// Sending a packet is how the helper does almost everything the player could do by hand:
/// use an item, throw one away, cast, speak. The alternative — calling the client's UI
/// routines — needs state that a thread arriving from outside does not have, and gets the
/// account disconnected when it turns out not to.
/// </para>
/// <para>
/// The code is position-independent. It finds itself with <c>call $+5; pop esi</c> and
/// reaches its own strings from there, so it runs correctly at whatever address it is
/// written to and nothing has to be told where that is.
/// </para>
/// <para>
/// The reference reached its strings with <c>add esi, disp8</c>, which is three bytes
/// shorter and stops working at 127 bytes — guarded by a <c>debug_assert!</c> that release
/// builds drop, in a builder that embeds a message the player typed. This uses the
/// four-byte form everywhere instead.
/// </para>
/// </remarks>
internal static class PacketCall
{
    /// <summary>How wide one argument is on the stack.</summary>
    private const int SlotSize = 4;

    /// <summary>
    /// Builds <c>SendPacketData(format, arguments…)</c> with the format carried along.
    /// </summary>
    /// <param name="format">The client's own format letters, without a terminator.</param>
    /// <param name="arguments">In the order the format names them.</param>
    internal static byte[] Send(ReadOnlySpan<byte> format, params PacketArgument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // The format string is the first argument, and it is a string like any other.
        PacketArgument[] all = [PacketArgument.Text8(format), .. arguments];

        // Nothing in this changes length with the displacements, so building it once
        // against a placeholder says where the code ends and the strings begin.
        var codeLength = Emit(all, new int[all.Length]).Length;

        var whole = new List<byte>(Emit(all, TextOffsets(all, codeLength)));

        // The strings follow, in the order the arguments name them, so a reader of the
        // bytes meets the format first.
        foreach (var text in all.Select(a => a.Text).OfType<byte[]>())
        {
            whole.AddRange(text);
            whole.Add(0);
        }

        return [.. whole];
    }

    /// <summary>
    /// Builds <c>SendPacketData(format, arguments…)</c> using a format string the client
    /// already has.
    /// </summary>
    /// <remarks>
    /// Nothing needs an address of its own then, so this is a plain run of pushes with no
    /// position-independence machinery at all.
    /// </remarks>
    internal static byte[] Send(GameAddress format, params PacketArgument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (Array.Exists(arguments, a => a.Text is not null))
        {
            throw new ArgumentException(
                "A string argument needs somewhere to live, which only the carried-format form has.",
                nameof(arguments));
        }

        var code = new ShellcodeBuilder(new GameAddress(0)).PushAd();

        PushArguments(code, arguments, [], 0);

        return Finish(code, PacketArgument.Number(format.Value), [], 0, arguments.Length + 1);
    }

    /// <summary>Where each string ends up, relative to what <c>pop esi</c> leaves behind.</summary>
    private static int[] TextOffsets(PacketArgument[] arguments, int codeLength)
    {
        var offsets = new int[arguments.Length];
        var at = codeLength;

        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].Text is not { } text)
            {
                continue;
            }

            offsets[i] = at;
            at += text.Length + 1;
        }

        return offsets;
    }

    private static byte[] Emit(PacketArgument[] arguments, int[] offsets)
    {
        var code = new ShellcodeBuilder(new GameAddress(0)).PushAd();

        // The call pushes the address of what follows it, which is the pop itself — so esi
        // ends up holding where the pop sits, and that is what every offset below is
        // measured from.
        code.CallTo(code.CurrentAddress + 5);

        var origin = code.Length;

        code.PopEsi();

        PushArguments(code, arguments[1..], offsets[1..], origin);

        return Finish(code, arguments[0], offsets, origin, arguments.Length);
    }

    /// <summary>Pushes right to left, as a C caller does.</summary>
    private static void PushArguments(
        ShellcodeBuilder code, PacketArgument[] arguments, int[] offsets, int origin)
    {
        for (var i = arguments.Length - 1; i >= 0; i--)
        {
            Push(code, arguments[i], offsets.Length == 0 ? 0 : offsets[i], origin);
        }
    }

    private static void Push(ShellcodeBuilder code, PacketArgument argument, int offset, int origin)
    {
        if (argument.Text is not null)
        {
            code.LeaEaxEsi(offset - origin).PushEax();
        }
        else if (argument.Indirect is { } address)
        {
            code.PushPtr(address);
        }
        else
        {
            code.PushImm32(argument.Value);
        }
    }

    private static byte[] Finish(
        ShellcodeBuilder code, PacketArgument format, int[] offsets, int origin, int slots)
    {
        Push(code, format, offsets.Length == 0 ? 0 : offsets[0], origin);

        return code
            .MovEax(GameFunctions.SendPacketData.Value)
            .CallEax()

            // Variadic C, so the caller takes the arguments back off.
            .AddEsp((byte)(slots * SlotSize))
            .PopAd()
            .Ret()
            .Build();
    }
}
