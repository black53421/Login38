using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Login38.Aux.Settings;

/// <summary>
/// Keeps one settings file per character.
/// </summary>
/// <remarks>
/// <para>
/// Settings belong to a character rather than to an installation: what a mage wants topped
/// up is not what a knight wants, and a player who moves between them should not have to
/// set it up twice. So the file is named after the character, read when they enter the
/// world and written when they leave it.
/// </para>
/// <para>
/// Nothing here throws. It runs while a game is being played, and a settings file that
/// cannot be read is worth a default and a line in the log — not a launcher that stops.
/// </para>
/// </remarks>
public sealed class ProfileStore
{
    /// <summary>Where the files live, beside the launcher.</summary>
    /// <remarks>
    /// Deliberately not somewhere tidy under the user's profile. Players are told to look
    /// here, they edit these by hand, and they copy them between installations.
    /// </remarks>
    public const string DirectoryName = "aux_settings";

    private const string Extension = ".json";

    /// <summary>Names Windows will not give a file, whatever they are used for.</summary>
    /// <remarks>
    /// Reserved for devices. A character called <c>CON</c> would otherwise open the console
    /// rather than a file — the write appears to succeed and nothing is ever stored.
    /// </remarks>
    private static readonly string[] DeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private readonly string _directory;
    private readonly ILogger<ProfileStore> _logger;

    /// <param name="directory">Where the files live. Defaults to beside the launcher.</param>
    public ProfileStore(ILogger<ProfileStore> logger, string? directory = null)
    {
        _logger = logger;
        _directory = directory ?? Path.Combine(AppContext.BaseDirectory, DirectoryName);
    }

    /// <summary>Where a character's settings would be.</summary>
    public string PathFor(string character) =>
        Path.Combine(_directory, SafeFileName(character) + Extension);

    /// <summary>Reads a character's settings, or the defaults if there are none.</summary>
    public AuxSettings Load(string character)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(character);

        var path = PathFor(character);

        if (!File.Exists(path))
        {
            _logger.LogInformation("{Character} has no settings yet; starting from the defaults", character);
            return new AuxSettings { Profile = character }.Normalise();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AuxSettings>(
                File.ReadAllText(path), AuxSettingsJson.Options);

            if (settings is null)
            {
                throw new JsonException("The file holds null.");
            }

            settings.Profile = character;
            _logger.LogInformation("Loaded {Character}'s settings from {Path}", character, path);

            return settings.Normalise();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Not overwritten. Whatever is wrong with it, the player's own file is more
            // likely to be recoverable by hand than to be worth replacing with defaults.
            _logger.LogWarning(e, "Could not read {Path}; using the defaults for this session", path);

            return new AuxSettings { Profile = character }.Normalise();
        }
    }

    /// <summary>Writes a character's settings.</summary>
    /// <returns>False when it could not be written, which is already logged.</returns>
    public bool Save(string character, AuxSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(character);
        ArgumentNullException.ThrowIfNull(settings);

        var path = PathFor(character);

        try
        {
            Directory.CreateDirectory(_directory);

            // Written beside the real file and moved over it, so a launcher that is killed
            // mid-write leaves the previous settings rather than half of the new ones.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, AuxSettingsJson.Options));
            File.Move(temporary, path, overwrite: true);

            _logger.LogInformation("Saved {Character}'s settings to {Path}", character, path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(e, "Could not save {Character}'s settings to {Path}", character, path);
            return false;
        }
    }

    /// <summary>
    /// Turns a character's name into a file name Windows will accept.
    /// </summary>
    /// <remarks>
    /// A character name can hold anything the server allowed, which is not the same set as
    /// a file name allows. Three things have to be dealt with and the reference dealt with
    /// one: the illegal characters, the names reserved for devices, and the trailing dots
    /// and spaces Windows silently drops — which would quietly point two characters at one
    /// file.
    /// </remarks>
    public static string SafeFileName(string character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var illegal = Path.GetInvalidFileNameChars();
        var safe = new char[character.Length];

        for (var i = 0; i < character.Length; i++)
        {
            var c = character[i];
            safe[i] = Array.IndexOf(illegal, c) >= 0 || char.IsControl(c) ? '_' : c;
        }

        var name = new string(safe).TrimEnd('.', ' ');

        if (name.Length == 0)
        {
            return "_";
        }

        // Compared against the part before the first dot, which is what Windows matches a
        // device name on: `CON.json` is the console just as much as `CON` is.
        var stem = name.Split('.')[0];

        return Array.Exists(DeviceNames, device => stem.Equals(device, StringComparison.OrdinalIgnoreCase))
            ? "_" + name
            : name;
    }
}
