using System.Text;
using Login38.Core.Morph.SmoothRun;

namespace Login38.Core.Morph;

/// <summary>Where a morph table was found and what form it was in.</summary>
/// <param name="Path">The file that will be read.</param>
/// <param name="IsPackage">Whether it is an encoder package rather than plain text.</param>
public readonly record struct MorphTableSource(string Path, bool IsPackage);

/// <summary>A morph table, ready to be written into the client.</summary>
/// <param name="Bytes">The buffer, marker byte and all.</param>
/// <param name="SmoothRun">
/// What the run-cycle pass changed, or null when it was not run.
/// </param>
public readonly record struct MorphTable(byte[] Bytes, SmoothRunReport? SmoothRun);

/// <summary>
/// Finds the morph table that belongs to a client.
/// </summary>
/// <remarks>
/// The table sits beside the client and shares its name: <c>TW13081901.bin</c> is served by
/// <c>TW13081901.pak</c>, or by <c>TW13081901.txt</c> while an operator is still editing it.
/// </remarks>
public static class MorphTableFile
{
    private const string PackageExtension = ".pak";

    private const string TextExtension = ".txt";

    /// <summary>
    /// Picks the table to load, or null when the operator has not published one.
    /// </summary>
    /// <param name="gameDirectory">Where the client lives.</param>
    /// <param name="executableName">The client's file name, extension and all.</param>
    /// <param name="chosen">
    /// The table the operator picked, by name and without its extension. Empty or null
    /// means the one named after the client, which is what an operator who has only ever
    /// had one table has always had.
    /// </param>
    /// <remarks>
    /// <para>
    /// The package wins when it is one this launcher will accept. A package that fails its
    /// tag is passed over rather than treated as an error, so an operator who leaves a
    /// stale or third-party <c>.pak</c> in the directory still gets the text file they are
    /// working on — and finds out about the bad package from the log rather than from a
    /// client that will not start.
    /// </para>
    /// <para>
    /// A chosen name that is not there finds nothing. It does not quietly become the
    /// client-named table: an operator who asked for one table and got another would have
    /// no way to tell, and the whole point of choosing is that the choice is kept.
    /// </para>
    /// <para>
    /// Only the name is taken from what was chosen. A path, absolute or with directories
    /// in it, would let a server list reach outside the game's own directory.
    /// </para>
    /// </remarks>
    public static MorphTableSource? Locate(
        string gameDirectory, string executableName, string? chosen = null)
    {
        ArgumentNullException.ThrowIfNull(gameDirectory);
        ArgumentNullException.ThrowIfNull(executableName);

        var name = string.IsNullOrWhiteSpace(chosen)
            ? Path.GetFileNameWithoutExtension(executableName)
            : Path.GetFileNameWithoutExtension(chosen.Trim());

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var stem = Path.Combine(gameDirectory, name);
        var package = stem + PackageExtension;

        if (MorphPackage.IsPackage(package))
        {
            return new MorphTableSource(package, IsPackage: true);
        }

        var text = stem + TextExtension;

        return File.Exists(text) ? new MorphTableSource(text, IsPackage: false) : null;
    }

    /// <summary>
    /// Whether to hook the client's morph loading at all.
    /// </summary>
    /// <param name="requested">The operator's <c>transform_file</c> setting.</param>
    /// <param name="source">What <see cref="Locate"/> found.</param>
    /// <remarks>
    /// A published package turns the feature on by itself. An operator who has gone to the
    /// trouble of building one has decided; leaving it inert because a separate switch was
    /// never flipped is a support call, not a safeguard. A plain text file is not treated
    /// that way — it is what sits in the directory while someone is editing.
    /// </remarks>
    public static bool ShouldHook(bool requested, MorphTableSource? source) =>
        source is { } found && (requested || found.IsPackage);

    /// <summary>
    /// Reads a table and, unless asked not to, folds run cycles into it.
    /// </summary>
    /// <param name="source">What <see cref="Locate"/> found.</param>
    /// <param name="smoothRun">Whether to run the run-cycle pass.</param>
    /// <remarks>
    /// The pass applies whichever form the table arrived in. The reference ran it only on
    /// packages, so an operator testing against their working text file got a client with
    /// the run-cycle hook installed and no run cycles for it to play — the one configuration
    /// in which the feature looks broken rather than absent.
    /// </remarks>
    public static MorphTable Load(MorphTableSource source, bool smoothRun = true)
    {
        var raw = MorphPackage.Load(source.Path);

        if (!smoothRun || raw.Length < 2 || raw[0] != MorphPackage.Marker)
        {
            return new MorphTable(raw, null);
        }

        // The client reads this in its own code page, and the pass only ever moves whole
        // lines and writes ASCII digits — so the bytes it does not understand have to
        // survive it unchanged. Latin-1 is the round-trip that guarantees that.
        var text = MorphText.GetString(raw.AsSpan(1));
        var (processed, report) = SmoothRunPreprocessor.Process(text);

        return new MorphTable([MorphPackage.Marker, .. MorphText.GetBytes(processed)], report);
    }

    /// <summary>
    /// Byte-preserving text for a file whose encoding is the client's business.
    /// </summary>
    /// <remarks>
    /// Every byte maps to the character of the same value and back. Sprite names in the
    /// table are in the operator's own code page and this code has no reason to decode
    /// them — it only needs to move the lines they are on.
    /// </remarks>
    private static Encoding MorphText { get; } = Encoding.Latin1;
}
