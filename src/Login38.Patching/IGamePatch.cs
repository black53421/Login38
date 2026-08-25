namespace Login38.Patching;

/// <summary>When during a launch a patch can be applied.</summary>
/// <remarks>
/// Not a preference — each value is a precondition. Applying a patch before the client
/// has reached the state its target belongs to means writing over bytes that are still
/// packed, or over a structure that has not been allocated.
/// </remarks>
public enum PatchPhase
{
    /// <summary>As soon as the client's code has been decrypted.</summary>
    Startup,

    /// <summary>
    /// Once the client has loaded Winsock, which it does only when it is about to talk to
    /// a server.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Startup"/> because the wait is long and nothing else
    /// depends on it. The client loads Winsock about half a minute in, and while this sat
    /// among the startup patches every other patch — and the helper, which starts when
    /// they finish — waited behind it for a client that was already on screen.
    /// </remarks>
    Networking,

    /// <summary>Once the client has shown its window.</summary>
    WindowVisible,

    /// <summary>Once the player is in the world.</summary>
    InWorld,
}

/// <summary>
/// One modification applied to the running game.
/// </summary>
/// <remarks>
/// <para>
/// Patches are independent and mostly fail-soft: a signature that no longer matches in
/// a newer client build means that one feature is unavailable, not that the game cannot
/// start. <see cref="PatchPipeline"/> enforces that, so an implementation should just
/// throw when it cannot do its job.
/// </para>
/// <para>
/// The reference had each patch bundle log its own installed/skipped outcome. That is
/// the pipeline's job here, so implementations only describe and act.
/// </para>
/// </remarks>
public interface IGamePatch
{
    /// <summary>Stable identifier, used in logs and to report outcomes.</summary>
    string Name { get; }

    /// <summary>
    /// The earliest point in a launch at which this can be applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference ran the later ones from threads it spawned, each polling for the
    /// state it needed and each with its own timeout. Declaring the requirement instead
    /// lets one waiter serve every patch that shares it, and makes "this needs the player
    /// in the world" a property of the patch rather than something buried in a thread.
    /// </para>
    /// <para>
    /// Stated by every patch rather than defaulted: a patch that runs too early does not
    /// fail, it finds nothing and reports that the signature is missing.
    /// </para>
    /// </remarks>
    PatchPhase Phase { get; }

    /// <summary>
    /// Whether this patch is wanted for this launch. Usually a check against the
    /// operator's feature switches.
    /// </summary>
    bool ShouldApply(GamePatchContext context);

    /// <summary>
    /// Applies the patch, throwing if it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Must be safe to call against a process that has already been patched — check for
    /// an existing hook rather than assuming a clean target. Installing twice would
    /// capture the first hook's own jump as "original bytes" and loop forever.
    /// </para>
    /// <para>
    /// Several patches wait on the client reaching some state, for up to two minutes.
    /// The token is how closing the launcher mid-launch stops them, so any wait longer
    /// than an instant has to observe it.
    /// </para>
    /// </remarks>
    void Apply(GamePatchContext context, CancellationToken cancellationToken = default);
}
