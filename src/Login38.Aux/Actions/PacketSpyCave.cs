using Login38.Aux.Toggles;
using Login38.Interop;

namespace Login38.Aux.Actions;

/// <summary>
/// Records every packet the client sends, for working out what to send it.
/// </summary>
/// <remarks>
/// <para>
/// A detour on <c>SendPacketData</c> that writes the caller, the format string and the
/// arguments into a ring buffer in the client, which the launcher reads. Nothing in the
/// launcher's own features needs it: it is how the addresses and formats the rest of this
/// port hard-codes were found in the first place, and how the next client version's would
/// be found again.
/// </para>
/// <para>
/// The reference calls the first argument a packet buffer and copies thirty-two bytes of
/// "the actual packet contents" out of it. It is the format string — <c>cssddddddd</c> and
/// the like — which is considerably more useful, because format and arguments together are
/// exactly what a call has to be rebuilt from.
/// </para>
/// </remarks>
internal static class PacketSpyCave
{
    /// <summary>How many entries the ring holds.</summary>
    internal const int RingLength = 64;

    /// <summary>And how big each is.</summary>
    internal const int EntrySize = 128;

    /// <summary>Where the ring starts in the cave.</summary>
    internal const uint BufferOffset = 0;

    /// <summary>Where the write counter sits, immediately after it.</summary>
    internal const uint IndexOffset = RingLength * EntrySize;

    /// <summary>And where the code goes.</summary>
    internal const uint CodeOffset = IndexOffset + 0x20;

    /// <summary>How much is asked for.</summary>
    internal const int CaveSize = 0x4000;

    /// <summary>Written last, so a reader can tell a finished entry from a slot.</summary>
    internal const uint Magic = 0xFEED_FACE;

    /// <summary>
    /// How many stack slots are copied: the return address and fifteen arguments.
    /// </summary>
    /// <remarks>
    /// This is a variadic function, so how many it was actually called with is only known
    /// from the format string — which is the first of them. Fifteen covers every format the
    /// client uses; the ones past the end of a shorter call are the caller's own locals,
    /// read harmlessly and thrown away once the format has been decoded.
    /// </remarks>
    internal const int Slots = 16;

    /// <summary>How much of the format string is copied.</summary>
    internal const int HeadBytes = 32;

    /// <summary>Where in an entry the copied format string starts.</summary>
    internal const int HeadOffset = Slots * 4;

    /// <summary>Where the write counter's value at the time of the write is kept.</summary>
    internal const int SequenceOffset = EntrySize - 8;

    /// <summary>And the magic, last of all.</summary>
    internal const int MagicOffset = EntrySize - 4;

    /// <summary>
    /// The range a format string may be in.
    /// </summary>
    /// <remarks>
    /// The client's constants are in its own image. Anything else is a caller that was not
    /// passing a format at all, and following it inside a detour would fault in the middle
    /// of somebody's network code.
    /// </remarks>
    private const uint LowestFormat = 0x0040_0000;

    /// <inheritdoc cref="LowestFormat"/>
    private const uint HighestFormat = 0x1000_0000;

    /// <summary>
    /// The entry point of <c>SendPacketData</c>.
    /// </summary>
    /// <remarks>
    /// Eight bytes rather than five: the whole prologue, so what the detour replays is
    /// three complete instructions. The reference reads whatever is there and relocates it
    /// after a heuristic scan for relative branches; this refuses a client whose prologue
    /// is not the one it was written against, which is both stricter and simpler.
    /// </remarks>
    internal static HookSite Site => new(
        GameFunctions.SendPacketData,
        [0x55, 0x8B, 0xEC, 0xB8, 0x0C, 0x14, 0x00, 0x00],   // push ebp; mov ebp,esp; mov eax,0x140C
        GameFunctions.SendPacketData + 8);

    private const byte Eax = 0;
    private const byte Ebx = 3;

    /// <summary>
    /// Builds the recorder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot is claimed with a locked exchange-and-add before anything is written to it,
    /// not with an increment afterwards. The client sends from more than one thread, and
    /// the reference's order lets two of them compute the same slot, write over each other
    /// and then both increment — losing one packet and leaving the reader with two
    /// sequence numbers for one entry.
    /// </para>
    /// <para>
    /// The direction flag is cleared. Nothing guarantees its state inside a function this
    /// detour is standing in the middle of, and a <c>rep movsd</c> that runs backwards
    /// writes ninety-six bytes off the front of the ring rather than into it.
    /// </para>
    /// </remarks>
    internal static byte[] Build(GameAddress cave)
    {
        var code = new ShellcodeBuilder(cave + CodeOffset);
        var buffer = cave + BufferOffset;
        var index = cave + IndexOffset;

        code.PushAd()
            .PushFd()
            .Cld()
            .MovEax(1)
            .Bytes([0xF0, 0x0F, 0xC1, 0x05]).Dword(index.Value)   // lock xadd [index], eax
            .Bytes([0x89, ModRm(3, Eax, Ebx)])                    // mov ebx, eax   keep the sequence
            .Bytes([0x83, 0xE0, RingLength - 1])                  // and eax, 0x3F
            .Bytes([0xC1, 0xE0, Shift])                           // shl eax, 7     times the entry size
            .Byte(0x05).Dword(buffer.Value)                       // add eax, buffer
            .Bytes([0x89, ModRm(3, Eax, Edi)]);                   // mov edi, eax

        // pushad and pushfd put thirty-six bytes between esp and what the caller pushed.
        code.Bytes([0x8D, 0x74, 0x24, Pushed])                    // lea esi, [esp+0x24]
            .Bytes([0xB9]).Dword((uint)Slots)                           // mov ecx, 16
            .RepMovsd();

        // The format string, if the first argument looks like one.
        code.Bytes([0x8B, 0x74, 0x24, Pushed + 4])                // mov esi, [esp+0x28]
            .Bytes([0x85, ModRm(3, Esi, Esi)]);                   // test esi, esi
        var noFormat = code.ShortJumpIfZero();

        code.Bytes([0x81, ModRm(3, 7, Esi)]).Dword(LowestFormat); // cmp esi, 0x00400000
        var tooLow = code.ShortJump(Jb);
        code.Bytes([0x81, ModRm(3, 7, Esi)]).Dword(HighestFormat);
        var tooHigh = code.ShortJump(Jae);

        code.Bytes([0xB9]).Dword((uint)HeadBytes / 4)                   // mov ecx, 8
            .RepMovsd();
        var copied = code.ShortJumpAlways();

        code.MarkLabel(noFormat).MarkLabel(tooLow).MarkLabel(tooHigh)
            .Bytes([0x83, ModRm(3, 0, Edi), HeadBytes]);          // add edi, 32

        code.MarkLabel(copied)
            .Bytes([0x83, ModRm(3, 0, Edi), SequenceOffset - HeadOffset - HeadBytes])
            .Bytes([0x89, ModRm(0, Ebx, Edi)])                    // mov [edi], ebx
            .Bytes([0xC7, ModRm(1, 0, Edi), MagicOffset - SequenceOffset]).Dword(Magic);

        code.PopFd()
            .PopAd()
            .Bytes(Site.Stock)
            .JumpTo(Site.Resume);

        return code.Build();
    }

    /// <summary>Lays out an empty ring.</summary>
    internal static byte[] Empty() => new byte[CaveSize];

    private const byte Esi = 6;
    private const byte Edi = 7;
    private const byte Jb = 0x72;
    private const byte Jae = 0x73;

    /// <summary>How far <c>pushad</c> and <c>pushfd</c> moved the stack.</summary>
    private const byte Pushed = 0x24;

    /// <summary>Multiplying by the entry size, which is a power of two.</summary>
    private const byte Shift = 7;

    /// <summary>A mod/reg/rm byte.</summary>
    private static byte ModRm(byte mode, byte register, byte memory) =>
        (byte)((mode << 6) | (register << 3) | memory);
}
