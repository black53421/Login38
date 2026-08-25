using System.Net;
using Login38.Core.Servers;
using Login38.Interop;

namespace Login38.Patching;

/// <summary>
/// Everything a patch needs to know about the launch it is part of.
/// </summary>
public sealed class GamePatchContext
{
    /// <summary>
    /// The client's code and read-only data. Signature patches search inside this range;
    /// the game's own image ends well below the top of it.
    /// </summary>
    /// <remarks>
    /// Deliberately generous. The upper part holds the format strings the display patches
    /// rewrite, and an over-wide range costs a few unmapped chunks, whereas an over-narrow
    /// one silently fails to find its target.
    /// </remarks>
    public static readonly GameAddress ImageStart = new(0x0040_1000);

    /// <inheritdoc cref="ImageStart"/>
    public static readonly GameAddress ImageEnd = new(0x00A0_0000);

    private MemorySnapshot? _image;

    /// <param name="process">The running game.</param>
    /// <param name="gameDirectory">Where the client and its data files live.</param>
    /// <param name="aux">The operator's feature switches.</param>
    /// <param name="server">The server being connected to.</param>
    /// <param name="imageStart">Where signature scanning begins; defaults to <see cref="ImageStart"/>.</param>
    /// <param name="imageEnd">Where signature scanning ends; defaults to <see cref="ImageEnd"/>.</param>
    public GamePatchContext(
        RemoteProcess process,
        string gameDirectory,
        AuxConfig aux,
        ServerInfo server,
        GameAddress? imageStart = null,
        GameAddress? imageEnd = null)
    {
        Process = process;
        GameDirectory = gameDirectory;
        Aux = aux;
        Server = server;
        ScanStart = imageStart ?? ImageStart;
        ScanEnd = imageEnd ?? ImageEnd;
    }

    /// <summary>Where signature scanning begins.</summary>
    /// <remarks>
    /// A parameter rather than a constant because it is an observation about a particular
    /// client, and because a scan range that cannot be pointed anywhere else makes the
    /// patches that depend on it impossible to exercise without a running game.
    /// </remarks>
    public GameAddress ScanStart { get; }

    /// <inheritdoc cref="ScanStart"/>
    public GameAddress ScanEnd { get; }

    /// <summary>The running game.</summary>
    public RemoteProcess Process { get; }

    /// <summary>Where the client and its data files live.</summary>
    public string GameDirectory { get; }

    /// <summary>The operator's feature switches.</summary>
    public AuxConfig Aux { get; }

    /// <summary>The server being connected to.</summary>
    public ServerInfo Server { get; }

    /// <summary>
    /// Where the client is actually pointed, when that is not the server itself.
    /// </summary>
    /// <remarks>
    /// Set when packet encryption puts a loopback relay in between. Everything else about
    /// the launch still refers to the real server — this is only the address the winsock
    /// redirect writes.
    /// </remarks>
    public IPEndPoint? ConnectTarget { get; set; }

    /// <summary>
    /// One shared copy of the client image, captured on first use and kept in step by
    /// <see cref="MemorySnapshot.Apply"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twenty-odd patches search the same few megabytes. Reading it once instead of once
    /// per signature turns startup scanning from tens of cross-process megabytes into one
    /// pass, which matters because most of that scanning happens while the game is
    /// suspended.
    /// </para>
    /// <para>
    /// The client arrives packed, so this must not be captured before the protection
    /// bypass has seen decryption finish — a snapshot of encrypted bytes would make every
    /// later signature miss. That patch calls <see cref="InvalidateImage"/> when it is
    /// done, so the ordering enforces itself.
    /// </para>
    /// </remarks>
    public MemorySnapshot Image => _image ??= MemorySnapshot.Capture(Process, ScanStart, ScanEnd);

    /// <summary>Drops the cached image so the next reader captures the current bytes.</summary>
    public void InvalidateImage() => _image = null;

    /// <summary>
    /// The game's main window, once it has one.
    /// </summary>
    /// <remarks>
    /// Null until the client shows a window, which is several seconds into a launch.
    /// Patches that need it declare <see cref="PatchPhase.WindowVisible"/>, so by the time
    /// they run it has been set.
    /// </remarks>
    public GameWindow? Window { get; set; }

    /// <summary>
    /// Whether the in-process present hook is running in the game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set by <c>PresentHookPatch</c>, read by the patches that fix the same symptoms a
    /// different way and would conflict with it.
    /// </para>
    /// <para>
    /// Recorded as what actually happened rather than as what was asked for. The reference
    /// gated the alternatives on the environment variable that requests the hook, so a
    /// failed injection left the player with neither the hook nor the fallback — the one
    /// case where the fallback is most needed.
    /// </para>
    /// </remarks>
    public bool PresentHookInstalled { get; set; }

    /// <summary>
    /// Whether the client is reading its morph table from the launcher's buffer.
    /// </summary>
    /// <remarks>
    /// Read by the patches that depend on the table having been preprocessed on the way in.
    /// The reference set the equivalent flag to whether the hook had been *asked for*, and
    /// never joined the thread that would have said whether it worked.
    /// </remarks>
    public bool MorphTableInstalled { get; set; }

    /// <summary>
    /// Whether that table had run cycles folded into slots 98 and 99.
    /// </summary>
    /// <remarks>
    /// The hook that plays them has nothing to play without this, so it gates on it rather
    /// than on the setting that asks for the pass.
    /// </remarks>
    public bool MorphTableHasRunCycles { get; set; }
}
