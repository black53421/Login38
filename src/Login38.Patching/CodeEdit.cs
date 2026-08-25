using Login38.Interop;

namespace Login38.Patching;

/// <summary>What happened at one signature site.</summary>
public enum SiteStatus
{
    /// <summary>The signature is not in this client.</summary>
    NotFound,

    /// <summary>The bytes were changed.</summary>
    Patched,

    /// <summary>The bytes already held the patched value.</summary>
    AlreadyPatched,
}

/// <param name="Status">What happened.</param>
/// <param name="Address">Where, when the signature was found.</param>
public readonly record struct SiteEdit(SiteStatus Status, GameAddress Address)
{
    /// <summary>Whether the client now has the patched bytes, however they got there.</summary>
    public bool Effective => Status is SiteStatus.Patched or SiteStatus.AlreadyPatched;
}

/// <summary>
/// Rewrites a few bytes of the client, either at a signature or at a known address.
/// </summary>
/// <remarks>
/// <para>
/// Most of the patches here are one flipped branch: find a shape, change a byte, done.
/// Doing that by hand means repeating a scan, a bounds check, an idempotency check and a
/// verification read in every patch, which is where the interesting mistakes live.
/// </para>
/// <para>
/// The signatures deliberately wildcard the byte being replaced. Matching the original
/// value instead would make every patch a one-shot: after the first run the signature no
/// longer matches its own site, so a second attempt reports "not found" and cannot tell
/// that apart from a client it does not understand.
/// </para>
/// </remarks>
public static class CodeEdit
{
    /// <summary>Where a runtime value goes in a signature template.</summary>
    public const string ImmediatePlaceholder = "{0}";

    /// <summary>Fills a signature template with a concrete value.</summary>
    public static BytePattern Fill(string template, ReadOnlySpan<byte> immediate)
    {
        ArgumentNullException.ThrowIfNull(template);

        return BytePattern.Parse(
            template.Replace(ImmediatePlaceholder, BytePattern.Format(immediate), StringComparison.Ordinal));
    }

    /// <summary>
    /// Rewrites an immediate inside a signature that is only unique because of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wildcarding <see cref="Replace(GamePatchContext, BytePattern, string, int, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    /// relies on does not work when the value being replaced is what makes the signature
    /// identify anything: <c>push imm32; call; add esp,4</c> with the immediate blanked
    /// out matches most of the client.
    /// </para>
    /// <para>
    /// So the value stays in the signature, and the already-patched case is a second
    /// search for the same shape holding the new value instead. That distinction matters
    /// because "the site is not here" and "the site is here and already done" call for
    /// opposite responses.
    /// </para>
    /// </remarks>
    /// <param name="context">The launch being patched.</param>
    /// <param name="template">Signature text with <see cref="ImmediatePlaceholder"/> where the value goes.</param>
    /// <param name="description">What the site is, for error messages.</param>
    /// <param name="immediateOffset">Where the value starts, from the start of the match.</param>
    /// <param name="original">The value the client was built with.</param>
    /// <param name="replacement">The value to write.</param>
    public static SiteEdit ReplaceImmediate(
        GamePatchContext context,
        string template,
        string description,
        int immediateOffset,
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> replacement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (original.Length != replacement.Length)
        {
            throw new ArgumentException(
                $"{description}: an immediate cannot change width without moving the code after it.",
                nameof(replacement));
        }

        var alreadyPatched = context.Image.FindOnly(Fill(template, replacement), description);

        if (alreadyPatched is { } done)
        {
            return new SiteEdit(SiteStatus.AlreadyPatched, done + immediateOffset);
        }

        if (context.Image.FindOnly(Fill(template, original), description) is not { } match)
        {
            return new SiteEdit(SiteStatus.NotFound, GameAddress.Zero);
        }

        var site = match + immediateOffset;
        context.Image.Apply(context.Process, site, replacement);
        return new SiteEdit(SiteStatus.Patched, site);
    }

    /// <summary>Replaces bytes at a fixed offset inside a unique signature.</summary>
    /// <param name="context">The launch being patched.</param>
    /// <param name="pattern">Signature, with the edited bytes wildcarded.</param>
    /// <param name="description">What the site is, for error messages.</param>
    /// <param name="offset">Offset of the edit from the start of the match.</param>
    /// <param name="original">The bytes expected before patching.</param>
    /// <param name="patched">The bytes to write.</param>
    /// <exception cref="GameProcessException">
    /// The site was found but holds neither the original nor the patched bytes, so this
    /// is not the instruction the signature was written for.
    /// </exception>
    public static SiteEdit Replace(
        GamePatchContext context,
        BytePattern pattern,
        string description,
        int offset,
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> patched)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pattern);

        if (original.Length != patched.Length)
        {
            throw new ArgumentException(
                $"{description}: replacing {original.Length} bytes with {patched.Length} would shift the code after it.",
                nameof(patched));
        }

        var image = context.Image;

        if (image.FindOnly(pattern, description) is not { } match)
        {
            return new SiteEdit(SiteStatus.NotFound, GameAddress.Zero);
        }

        var site = match + offset;
        Span<byte> current = stackalloc byte[original.Length];

        if (!image.TryRead(site, current))
        {
            throw new GameProcessException($"{description} at {site} could not be read back.");
        }

        if (current.SequenceEqual(patched))
        {
            return new SiteEdit(SiteStatus.AlreadyPatched, site);
        }

        if (!current.SequenceEqual(original))
        {
            throw new GameProcessException(
                $"{description} at {site} holds {Describe(current)}, expected {Describe(original)} " +
                $"or the already-patched {Describe(patched)}.");
        }

        image.Apply(context.Process, site, patched);
        return new SiteEdit(SiteStatus.Patched, site);
    }

    /// <inheritdoc cref="Replace(GamePatchContext, BytePattern, string, int, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
    public static SiteEdit Replace(
        GamePatchContext context,
        string pattern,
        string description,
        int offset,
        byte original,
        byte patched) =>
        Replace(context, BytePattern.Parse(pattern), description, offset, [original], [patched]);

    /// <summary>
    /// Rewrites bytes at an address that is already known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some sites cannot be described by a signature worth having: a byte inside a format
    /// string, or a function prologue that looks like every other function prologue. Those
    /// are addressed directly, and the expected bytes are the only evidence that the
    /// address still means what it meant when it was written down.
    /// </para>
    /// <para>
    /// A site holding neither value is skipped rather than thrown, because these come in
    /// groups of twenty where the client has moved a handful and the rest are still
    /// right. The caller decides what a given number of misses means.
    /// </para>
    /// </remarks>
    public static SiteEdit ReplaceAt(
        GamePatchContext context,
        GameAddress address,
        string description,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> replacement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (expected.Length != replacement.Length)
        {
            throw new ArgumentException(
                $"{description}: replacing {expected.Length} bytes with {replacement.Length} would shift the code after it.",
                nameof(replacement));
        }

        Span<byte> current = stackalloc byte[expected.Length];

        if (!context.Image.TryRead(address, current))
        {
            context.Process.ReadBytes(address, current);
        }

        if (current.SequenceEqual(replacement))
        {
            return new SiteEdit(SiteStatus.AlreadyPatched, address);
        }

        if (!current.SequenceEqual(expected))
        {
            return new SiteEdit(SiteStatus.NotFound, address);
        }

        context.Image.Apply(context.Process, address, replacement);
        return new SiteEdit(SiteStatus.Patched, address);
    }

    /// <summary>
    /// Diverts a known address to <paramref name="target"/>, replacing exactly the bytes
    /// the caller says are there.
    /// </summary>
    /// <remarks>
    /// The jump is five bytes and the site is usually longer, so the remainder is filled
    /// with <c>nop</c>. Leaving the tail of a part-overwritten instruction behind would be
    /// executed as whatever those bytes happen to decode to on the way back.
    /// </remarks>
    public static SiteEdit Divert(
        GamePatchContext context,
        GameAddress address,
        string description,
        ReadOnlySpan<byte> expected,
        GameAddress target)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (expected.Length < InlineHook.JumpSize)
        {
            throw new ArgumentException(
                $"{description}: a jump needs {InlineHook.JumpSize} bytes and only {expected.Length} were claimed.",
                nameof(expected));
        }

        // Already diverted, possibly somewhere else. Re-reading the site as if it held the
        // original instructions would capture this jump as the bytes to restore.
        Span<byte> current = stackalloc byte[expected.Length];
        if (context.Image.TryRead(address, current) && current[0] == 0xE9)
        {
            return new SiteEdit(SiteStatus.AlreadyPatched, address);
        }

        return ReplaceAt(
            context, address, description, expected, InlineHook.BuildJump(address, target, expected.Length));
    }

    private static string Describe(ReadOnlySpan<byte> bytes) => BytePattern.Format(bytes);
}
