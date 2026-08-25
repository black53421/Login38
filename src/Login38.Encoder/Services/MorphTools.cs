using System.IO;
using System.Text;
using Login38.Core.Morph;
using Login38.Core.Text;

namespace Login38.Encoder.Services;

/// <summary>What encoding a morph table produced.</summary>
/// <param name="Source">Where the table was read from.</param>
/// <param name="Output">Where the package was written.</param>
/// <param name="TableBytes">How large the table was.</param>
/// <param name="PackageBytes">And the package.</param>
public readonly record struct MorphEncodeReport(
    string Source, string Output, long TableBytes, long PackageBytes)
{
    /// <summary>How much smaller the package is, as a percentage.</summary>
    /// <remarks>Negative for a table too small or too random to be worth deflating.</remarks>
    public double Saved => TableBytes == 0 ? 0 : (1 - ((double)PackageBytes / TableBytes)) * 100;
}

/// <summary>
/// The operator's side of the morph table: pack one, check one, and read the server's
/// animation table out of one.
/// </summary>
/// <remarks>
/// The format itself lives in <see cref="MorphPackage"/>, with the launcher's reader, so
/// that the two halves are written together and round-trip in one test. What is here is
/// only what the operator does with it.
/// </remarks>
public static class MorphTools
{
    /// <summary>What the animation table is called when it is written out.</summary>
    public const string SprActionSqlName = "spr_action.sql";

    /// <summary>What a packed table is called.</summary>
    public const string PackageExtension = ".pak";

    /// <summary>
    /// Packs a morph table for the launcher.
    /// </summary>
    /// <remarks>
    /// The table goes in exactly as it was written, whatever it is encoded in. Everything
    /// that reads it later — the launcher, the client — has its own opinion about the code
    /// page, and a guess made here would be a guess baked into the file.
    /// </remarks>
    /// <exception cref="MorphPackageException">The source is already a package.</exception>
    public static MorphEncodeReport Encode(string source, string output, bool compress = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        // Packing a package again produces something that decrypts to ciphertext, and the
        // launcher rejects it a long way from here.
        if (IsPackageName(source))
        {
            throw new MorphPackageException(
                "這已經是打包過的變身檔了。請選它原本的文字檔。");
        }

        var table = File.ReadAllBytes(source);
        var package = MorphPackage.Encrypt(table, compress);

        File.WriteAllBytes(output, package);

        return new MorphEncodeReport(source, output, table.Length, package.Length);
    }

    /// <summary>Whether a file is one of ours, by the tag on the end of it.</summary>
    public static bool IsOurs(string path) => MorphPackage.IsPackage(path);

    /// <summary>The packed tables in a directory that this encoder made.</summary>
    /// <remarks>
    /// By name, sorted, so that a list shown to an operator is the same list twice running.
    /// The reference took whatever order the file system handed it.
    /// </remarks>
    public static IReadOnlyList<string> Packages(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*" + PackageExtension)
                .Where(IsOurs)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Reads a morph table and writes the server's animation table beside it.
    /// </summary>
    /// <returns>How many rows were written.</returns>
    /// <exception cref="FormatException">The table holds no sprite at all.</exception>
    public static int WriteSprActionSql(ILegacyTextCodec codec, string source, string output)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        var result = SprActionSql.Generate(codec.ReadTextFile(source));

        // ASCII throughout — every value in it is a number — but written without a mark
        // all the same, because what imports this is a database client, not an editor.
        File.WriteAllText(output, result.Sql, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return result.Rows;
    }

    /// <summary>
    /// The packed tables in a directory that this encoder did <em>not</em> make.
    /// </summary>
    /// <remarks>
    /// So that an operator looking at a directory with a <c>.pak</c> in it and a list with
    /// nothing in it can be told why. Hiding a file that is plainly there is the same
    /// thing, to them, as not looking.
    /// </remarks>
    public static IReadOnlyList<string> ForeignPackages(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*" + PackageExtension)
                .Where(path => !IsOurs(path))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Where a packed table goes for a given source, if the operator says nothing.</summary>
    public static string OutputFor(string directory, string source) =>
        Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + PackageExtension);

    private static bool IsPackageName(string path) =>
        Path.GetExtension(path).Equals(PackageExtension, StringComparison.OrdinalIgnoreCase);
}
