using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace Login38.App.Notifications;

/// <summary>
/// Tells the game itself whether to draw the auto-hunt mark.
/// </summary>
/// <remarks>
/// <para>
/// The mark used to be drawn by the launcher, on a transparent window laid over the game.
/// That window is still there for everything else it draws, but the mark belongs in the
/// game's own picture: the launcher's present hook already owns the client's final blit, so
/// there is a device context with the finished frame on it going past every time the game
/// draws — and something drawn there is the same frame, the same size and the same place as
/// everything the client drew itself. Off the window it was a second animation running at a
/// second rate, which is what "the animation is not smooth enough" was.
/// </para>
/// <para>
/// The drawing is in the injected DLL. All that crosses from here is one byte, through a
/// named block the DLL makes when it installs its hooks — named after the game's own process
/// id, so two clients on one desktop do not switch each other's marks on.
/// </para>
/// <para>
/// Whether that block exists is also the answer to "is the in-game mark working": if the hook
/// was switched off, or the injection failed, nothing makes it, and the launcher keeps
/// drawing the mark on the overlay as before. See <see cref="IsLive"/>.
/// </para>
/// </remarks>
internal sealed class AtsMark : IDisposable
{
    /// <summary><c>L38A</c>, as the DLL writes it.</summary>
    private const uint Magic = 0x4C333841;

    private const uint Version = 1;

    private const int MagicAt = 0;
    private const int VersionAt = 4;
    private const int HuntingAt = 8;
    private const int Length = 12;

    /// <summary>How long to leave a missing block alone before looking again.</summary>
    /// <remarks>
    /// The DLL only makes it once the client has finished setting up DirectDraw, which is a
    /// good few seconds after the process exists, so the first several looks are expected to
    /// fail. Asking ten times a second for that whole stretch is a lot of nothing.
    /// </remarks>
    private static readonly TimeSpan Between = TimeSpan.FromSeconds(2);

    private readonly ILogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    private MemoryMappedFile? _block;
    private MemoryMappedViewAccessor? _view;
    private uint _opened;
    private DateTimeOffset _looked = DateTimeOffset.MinValue;
    private bool _said;
    private bool? _written;

    internal AtsMark(ILogger logger, Func<DateTimeOffset>? clock = null)
    {
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Whether the game is drawing the mark itself.</summary>
    /// <remarks>
    /// False until the block has been found, which means the overlay draws the mark for the
    /// first few seconds of a session and then stops. Both look the same on the screen.
    /// </remarks>
    internal bool IsLive => _view is not null;

    /// <summary>The name the DLL publishes its block under for a given client.</summary>
    internal static string NameFor(uint processId) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture, $"Local\\l38ats_{processId}");

    /// <summary>Says whether the hunt is running, to whichever client is being driven.</summary>
    internal void Set(uint processId, bool hunting)
    {
        if (_view is not null && _opened != processId)
        {
            // A different client. The old block belongs to a process that is either gone or
            // no longer ours to write to.
            Close();
        }

        if (_view is null)
        {
            Open(processId);
        }

        // Written on the pass it changes and on the pass the block is first found, and not
        // otherwise: this runs ten times a second for as long as the launcher is up.
        if (_view is null || _written == hunting)
        {
            return;
        }

        try
        {
            _view.Write(HuntingAt, hunting ? (byte)1 : (byte)0);
            _written = hunting;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                  or ObjectDisposedException)
        {
            _logger.LogDebug(e, "The game's own hunt mark could not be switched; closing it");
            Close();
        }
    }

    /// <summary>
    /// Puts the mark out and lets go of the block.
    /// </summary>
    /// <remarks>
    /// The game outlives the launcher often enough to matter — closing the launcher with the
    /// client still up is how people stop the helper. Nothing else would ever clear the byte,
    /// so the client would carry on drawing a mark for a hunt that is not running.
    /// </remarks>
    internal void Stop()
    {
        if (_view is not null && _written is true)
        {
            try
            {
                _view.Write(HuntingAt, (byte)0);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or ObjectDisposedException)
            {
                _logger.LogDebug(e, "The game's own hunt mark could not be put out");
            }
        }

        Close();
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    private void Open(uint processId)
    {
        var now = _clock();

        if (now - _looked < Between)
        {
            return;
        }

        _looked = now;

        try
        {
            var block = MemoryMappedFile.OpenExisting(
                NameFor(processId), MemoryMappedFileRights.ReadWrite);
            var view = block.CreateViewAccessor(0, Length, MemoryMappedFileAccess.ReadWrite);

            if (view.ReadUInt32(MagicAt) != Magic || view.ReadUInt32(VersionAt) != Version)
            {
                // Something else has the name. Writing into it would be writing into a
                // stranger's memory, so leave it and keep drawing on the overlay.
                view.Dispose();
                block.Dispose();

                if (!_said)
                {
                    _said = true;
                    _logger.LogWarning(
                        "{Name} is not the game's hunt mark; drawing it on the overlay instead",
                        NameFor(processId));
                }

                return;
            }

            _block = block;
            _view = view;
            _opened = processId;
            _said = false;

            _logger.LogInformation("The game is drawing the hunt mark itself");
        }
        catch (Exception e) when (e is FileNotFoundException or IOException
                                  or UnauthorizedAccessException)
        {
            // Expected while the client is still starting, and expected for as long as
            // anybody runs with the present hook switched off.
            _logger.LogTrace(e, "The game's own hunt mark is not published yet");
        }
    }

    private void Close()
    {
        _view?.Dispose();
        _block?.Dispose();
        _view = null;
        _block = null;
        _opened = 0;
        _written = null;
        _looked = DateTimeOffset.MinValue;
    }
}
