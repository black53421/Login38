using System.IO;

namespace Login38.Encoder.Services;

/// <summary>
/// The looks an operator can give the launcher.
/// </summary>
/// <remarks>
/// One directory under <c>skins/</c> per look, each holding an <c>index.html</c> and
/// whatever it draws itself with. Which one is in use is written into the settings, so a
/// player gets the operator's without doing anything.
/// </remarks>
public static class SkinCatalog
{
    /// <summary>Where they live, beside the encoder.</summary>
    public const string DirectoryName = "skins";

    /// <summary>What makes a directory one of them.</summary>
    public const string IndexName = "index.html";

    /// <summary>What the background is called inside one.</summary>
    public const string BackgroundName = "bg.jpg";

    /// <summary>The one every installation has.</summary>
    public const string Default = "default";

    /// <summary>Where they are, for a given installation.</summary>
    public static string DirectoryIn(string root) => Path.Combine(root, DirectoryName);

    /// <summary>Where one of them is.</summary>
    public static string PathFor(string root, string skin) => Path.Combine(DirectoryIn(root), skin);

    /// <summary>
    /// The looks that are installed.
    /// </summary>
    /// <remarks>
    /// A directory counts only when it holds an <c>index.html</c>, because that is what the
    /// launcher opens — an empty directory in the list is a look that shows nothing. There
    /// is always at least one name, so a combo box is never empty.
    /// </remarks>
    public static IReadOnlyList<string> Scan(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var directory = DirectoryIn(root);

        if (!Directory.Exists(directory))
        {
            return [Default];
        }

        string[] found =
        [
            .. Directory.EnumerateDirectories(directory)
                .Where(path => File.Exists(Path.Combine(path, IndexName)))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        return found.Length == 0 ? [Default] : found;
    }

    /// <summary>
    /// Puts an image in as one look's background.
    /// </summary>
    /// <returns>Where it was written.</returns>
    /// <exception cref="DirectoryNotFoundException">There is no such look installed.</exception>
    public static string ApplyBackground(string root, string skin, string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(skin);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var directory = PathFor(root, skin);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"{DirectoryIn(root)} 底下沒有叫 {skin} 的外觀。");
        }

        var target = Path.Combine(directory, BackgroundName);

        File.Copy(image, target, overwrite: true);

        return target;
    }
}
