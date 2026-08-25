using Login38.Core.Text;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Actions;

/// <summary>
/// Writes a line into the game's own chat window.
/// </summary>
/// <remarks>
/// <para>
/// The helper's way of telling the player something while they are looking at the game
/// rather than at the launcher. It goes through the client's own routine rather than into
/// the buffer directly, because the routine also does the scrolling — written straight into
/// the buffer, a line appears and the window stops following the bottom.
/// </para>
/// <para>
/// The reference allocates a page for every line and never frees any of them, on the
/// grounds that the client may still be referring to the text after the thread has
/// finished. That reasoning is sound and the conclusion is not: it is called once per
/// monster killed, so a long evening leaks thousands of pages. Here it is one page per
/// game, divided into slots used in turn, so a line is only overwritten after every other
/// slot has been used since.
/// </para>
/// </remarks>
public class GameChat
{
    /// <summary>The client's own "add a line to the chat window" routine.</summary>
    internal static readonly GameAddress Dispatch = new(0x00437500);

    /// <summary>
    /// Which channel to say it on.
    /// </summary>
    /// <remarks>
    /// Not a real channel. Minus one is the client's own value for "show this and do the
    /// scrolling and the sound", which is the path an ordinary incoming message takes.
    /// </remarks>
    internal const byte LocalChannel = 0xFF;

    /// <summary>Who it came from. Nobody, so the client draws no name.</summary>
    internal const ushort NoSource = 0xFFFF;

    /// <summary>Green, in the client's own colour format.</summary>
    internal const ushort Green = 0x07E0;

    /// <summary>
    /// The client's own mark for green, written at the front of a line.
    /// </summary>
    /// <remarks>
    /// Read by the routine that adds the line, before anything is drawn — which is why it
    /// is in the text rather than passed as the colour argument. Both work; this one does
    /// not depend on which argument the colour is.
    /// </remarks>
    public const string GreenMark = "\\F2";

    /// <summary>The client takes five arguments and cleans up after itself.</summary>
    internal const byte ArgumentBytes = 0x14;

    /// <summary>How many lines can be in flight before one is overwritten.</summary>
    internal const int Slots = 8;

    /// <summary>How much room each gets.</summary>
    internal const int SlotSize = 128;

    /// <summary>The most text one line can carry, leaving room for its terminator.</summary>
    internal const int LongestLine = SlotSize - 1;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<GameChat> _logger;

    private GameAddress? _page;
    private uint _processId;
    private int _next;

    public GameChat(ILegacyTextCodec codec, ILogger<GameChat> logger)
    {
        _codec = codec;
        _logger = logger;
    }

    /// <summary>Writes a line, in the client's own code page.</summary>
    /// <remarks>
    /// A line too long for a slot is cut rather than refused. This is a notice, and a
    /// shortened notice is better than none.
    /// </remarks>
    public virtual void Write(RemoteProcess process, string line)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentException.ThrowIfNullOrEmpty(line);

        var encoded = _codec.Encode(line, LegacyEncoding.Big5);
        var text = new byte[Math.Min(encoded.Length, LongestLine) + 1];

        encoded.AsSpan(0, text.Length - 1).CopyTo(text);

        try
        {
            var slot = Reserve(process);

            process.WriteBytes(slot, text);

            RemoteCall.Run(process, Build(slot), Timeout);
        }
        catch (GameProcessException e)
        {
            // Saying something is never worth failing a pass over.
            _logger.LogInformation(e, "The line could not be written into the game's chat window");
        }
    }

    /// <summary>
    /// The machine code that calls the client's routine.
    /// </summary>
    /// <remarks>
    /// Five arguments pushed in reverse, then cleaned up by this code rather than by the
    /// client — its own calling convention leaves them. The <c>ret 4</c> at the end is what
    /// a thread procedure owes its caller.
    /// </remarks>
    internal static byte[] Build(GameAddress text) =>
        // Position-independent — nothing here branches, so where it ends up does not
        // matter and the base address is never used.
        new ShellcodeBuilder(GameAddress.Zero)
            .PushImm32(0)
            .PushImm8(LocalChannel)
            .PushImm32(Green)
            .PushImm32(NoSource)
            .PushImm32(text)
            .MovEax(Dispatch.Value)
            .CallEax()
            .AddEsp(ArgumentBytes)
            .XorEaxEax()
            .RetAndPop(4)
            .Build();

    /// <summary>
    /// Takes the next slot, allocating the page the first time.
    /// </summary>
    /// <remarks>
    /// The page is never freed. The client may go on referring to the text after the thread
    /// that passed it has finished, and there is no way to be told when it has stopped —
    /// which is exactly why there is a ring of slots rather than one buffer reused. The
    /// code that calls the client is somewhere else and is freed normally; only the text
    /// has to outlive the call.
    /// </remarks>
    private GameAddress Reserve(RemoteProcess process)
    {
        if (_page is null || _processId != process.Id)
        {
            _page = process.AllocateExecutable(Slots * SlotSize);
            _processId = process.Id;
            _next = 0;
        }

        var slot = _page.Value + (_next * SlotSize);

        _next = (_next + 1) % Slots;

        return slot;
    }
}
