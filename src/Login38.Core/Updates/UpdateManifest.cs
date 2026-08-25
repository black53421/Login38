using System.Globalization;
using Login38.Core.Configuration;

namespace Login38.Core.Updates;

/// <summary>One incremental update package.</summary>
/// <param name="Version">The version this package upgrades <em>to</em>.</param>
/// <param name="Entry">A file name relative to the manifest, or an absolute URL.</param>
public readonly record struct UpdatePackage(uint Version, string Entry);

/// <summary>
/// The server's <c>Update.ini</c>: what the latest resource version is, and which
/// package carries each step up to it.
/// </summary>
/// <remarks>
/// <code>
/// [Update]
/// version=3
/// 1=foo.zip
/// 2=bar.zip
/// 3=baz.zip
/// </code>
/// <para>
/// <c>version</c> is the newest resource version the server has. Each numbered key is
/// the package that upgrades from <c>N-1</c> to <c>N</c>, so a client several versions
/// behind applies them in order. The section name is ignored; only key names matter.
/// </para>
/// </remarks>
public sealed class UpdateManifest
{
    private UpdateManifest(uint serverVersion, IReadOnlyList<UpdatePackage> packages, string sourceUrl)
    {
        ServerVersion = serverVersion;
        Packages = packages;
        SourceUrl = sourceUrl;
    }

    /// <summary>The newest resource version the server offers.</summary>
    public uint ServerVersion { get; }

    /// <summary>Packages in ascending version order, one per version.</summary>
    public IReadOnlyList<UpdatePackage> Packages { get; }

    /// <summary>Where the manifest was fetched from; relative entries resolve against it.</summary>
    public string SourceUrl { get; }

    /// <summary>
    /// Parses a manifest.
    /// </summary>
    /// <returns>
    /// Null when there is no <c>version</c> key. Without it there is nothing to compare
    /// the local version against, so the safe response is to skip updating rather than
    /// to guess.
    /// </returns>
    public static UpdateManifest? Parse(string content, string sourceUrl)
    {
        uint? serverVersion = null;
        var packages = new List<UpdatePackage>();

        foreach (var entry in IniReader.Read(content))
        {
            // IniReader lower-cases keys, so "Version" and "VER" both land here.
            if (entry.Key is "version" or "ver")
            {
                if (uint.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    serverVersion = parsed;
                }

                continue;
            }

            if (uint.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var version) &&
                !string.IsNullOrEmpty(entry.Value))
            {
                packages.Add(new UpdatePackage(version, entry.Value));
            }
        }

        if (serverVersion is not { } latest)
        {
            return null;
        }

        // Ascending, first entry wins on a duplicate version. OrderBy is a stable sort,
        // so "first" means first in file order — the same rule the reference used.
        var ordered = packages
            .OrderBy(package => package.Version)
            .DistinctBy(package => package.Version)
            .ToList();

        return new UpdateManifest(latest, ordered, sourceUrl);
    }

    /// <summary>Packages needed to go from <paramref name="localVersion"/> to the server's.</summary>
    public IEnumerable<UpdatePackage> PackagesAfter(uint localVersion) =>
        Packages.Where(package => package.Version > localVersion && package.Version <= ServerVersion);

    /// <summary>The absolute URL of a package entry.</summary>
    public string ResolveUrl(UpdatePackage package) => ResolveUrl(BaseUrlOf(SourceUrl), package.Entry);

    /// <summary>
    /// Everything up to and including the last <c>/</c> of a manifest URL. Empty when
    /// there is no slash at all.
    /// </summary>
    public static string BaseUrlOf(string manifestUrl)
    {
        var lastSlash = manifestUrl.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : manifestUrl[..(lastSlash + 1)];
    }

    /// <summary>Joins a base URL and an entry, passing absolute entries through.</summary>
    public static string ResolveUrl(string baseUrl, string entry) =>
        entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? entry
            : baseUrl + entry;
}
