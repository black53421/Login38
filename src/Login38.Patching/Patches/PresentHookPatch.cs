using System.Reflection;
using System.Security.Cryptography;
using Login38.Core;
using Login38.Interop;
using Microsoft.Extensions.Logging;

namespace Login38.Patching.Patches;

/// <summary>
/// Takes over how the client puts frames on the screen, so its own text boxes stay
/// visible.
/// </summary>
/// <remarks>
/// <para>
/// Windowed on Windows 11, the client's DirectDraw present blits over its child windows
/// every frame. Typing shows a black box that flickers, and the IME candidate list never
/// appears at all. The cause is a missing window style; the cure is either to add that
/// style, or to stop presenting that way.
/// </para>
/// <para>
/// The better cure is a small C++ DLL that hooks the client's present function and routes
/// it through a swapchain on the same window, letting the desktop compositor put the child
/// windows back on top. That DLL has to run inside the render loop, so it is the one piece
/// of this launcher that is injected rather than written from outside.
/// </para>
/// <para>
/// Setting <c>WS_CLIPCHILDREN</c> is the fallback, and only the fallback: with the hook
/// running, the swapchain already composes child windows and the style causes flicker of
/// its own.
/// </para>
/// </remarks>
public sealed class PresentHookPatch : IGamePatch
{
    /// <summary>Turns the whole thing off, falling back to the window style.</summary>
    public const string DisableVariable = "LOGIN38_DDRAW_INPROC";

    /// <summary>Injects a DLL from a path instead of the embedded copy, for development.</summary>
    public const string OverrideVariable = "LOGIN38_DDRAW_DLL";

    private const string DllName = "l38ddraw.dll";

    private const string ResourceName = "Login38.Patching.l38ddraw.dll";

    private readonly ILogger<PresentHookPatch> _logger;

    public PresentHookPatch(ILogger<PresentHookPatch> logger) => _logger = logger;

    /// <inheritdoc/>
    public string Name => "present-hook";

    /// <inheritdoc/>
    public PatchPhase Phase => PatchPhase.WindowVisible;

    /// <inheritdoc/>
    public bool ShouldApply(GamePatchContext context) => true;

    /// <inheritdoc/>
    public void Apply(GamePatchContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var window = context.Window
            ?? throw new GameProcessException("The game window is not known yet.");

        if (EnvironmentSwitch.IsDisabled(DisableVariable))
        {
            _logger.LogInformation("The present hook is switched off; falling back to the window style");
            FallBackToClipping(window);
            return;
        }

        try
        {
            var dll = ResolveDll();
            DllInjector.Inject(context.Process, dll);
            context.PresentHookInstalled = true;
            _logger.LogInformation("Present hook injected from {Path}", dll);
        }
        catch (Exception e) when (e is GameProcessException or IOException or UnauthorizedAccessException)
        {
            // The style is worth setting even so: it fixes the same symptom less well, and
            // leaving the player with a black input box because the better fix failed is
            // the wrong trade.
            _logger.LogWarning(e, "Could not inject the present hook; falling back to the window style");
            FallBackToClipping(window);
        }
    }

    private void FallBackToClipping(GameWindow window)
    {
        if (window.EnableChildClipping())
        {
            _logger.LogInformation("Added WS_CLIPCHILDREN to the game window");
        }
        else
        {
            _logger.LogDebug("The game window already clips its children");
        }
    }

    /// <summary>
    /// Finds the DLL to inject, writing out the embedded copy if there is nothing to
    /// override it.
    /// </summary>
    /// <remarks>
    /// Written under <c>%LOCALAPPDATA%</c> rather than beside the client: the game
    /// directory is often under Program Files, and this has to work without the launcher's
    /// elevation reaching the file system.
    /// </remarks>
    internal static string ResolveDll()
    {
        if (Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } overridePath &&
            File.Exists(overridePath))
        {
            return overridePath;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lineage38Launcher");

        Directory.CreateDirectory(directory);

        var payload = ReadEmbeddedDll();
        var path = Path.Combine(directory, DllName);

        if (!IsAlreadyWritten(path, payload))
        {
            try
            {
                File.WriteAllBytes(path, payload);
            }
            catch (IOException)
            {
                // A copy still loaded into a game from an earlier launch holds the file
                // open. A per-process name gives this launch something to inject without
                // disturbing the game that is using the other one.
                path = Path.Combine(directory, $"l38ddraw_{Environment.ProcessId}.dll");
                File.WriteAllBytes(path, payload);
            }
        }

        return path;
    }

    /// <summary>Whether the file on disk is already the one that would be written.</summary>
    /// <remarks>
    /// Compared rather than always rewritten, because rewriting fails while a game still
    /// has the previous copy loaded — which is the normal case when launching a second one.
    /// </remarks>
    private static bool IsAlreadyWritten(string path, byte[] payload)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var existing = File.ReadAllBytes(path);
            return existing.Length == payload.Length &&
                   CryptographicOperations.FixedTimeEquals(existing, payload);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static byte[] ReadEmbeddedDll()
    {
        using var stream = typeof(PresentHookPatch).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"{ResourceName} is not embedded in {typeof(PresentHookPatch).Assembly.GetName().Name}.");

        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
