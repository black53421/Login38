using System.IO;
using Login38.Core.Configuration;
using Login38.Core.Servers;
using Login38.Core.Text;
using Microsoft.Extensions.Logging;

namespace Login38.App.Services;

/// <summary>
/// Reads what the operator distributes with the client: the servers, and the switches
/// that decide which features the launcher turns on.
/// </summary>
/// <remarks>
/// <para>
/// Neither file is a player preference. They are replaced wholesale when the operator
/// publishes an update, and nothing here writes them back.
/// </para>
/// <para>
/// Two files, because the operator's encoder produces two. <c>list.txt</c> carries the
/// servers; <c>config.ini</c> carries the feature switches and the launcher's links. An
/// older package has only <c>list.txt</c>, with the switches inside it, so that is the
/// fallback rather than an error.
/// </para>
/// <para>
/// Both are read through the legacy codec: the encoder runs on a machine using the
/// traditional Chinese code page, and server names are the visible half of getting that
/// wrong.
/// </para>
/// </remarks>
public sealed class ServerCatalog
{
    /// <summary>The servers.</summary>
    public const string ServerListFileName = "list.txt";

    /// <summary>The feature switches and links, when the operator ships them separately.</summary>
    public const string SettingsFileName = "config.ini";

    private readonly ILegacyTextCodec _codec;
    private readonly ILogger<ServerCatalog> _logger;

    /// <param name="codec">Decodes the operator's code page.</param>
    /// <param name="logger">Where to report what was loaded.</param>
    /// <param name="directory">Where the files are; defaults to beside the launcher.</param>
    public ServerCatalog(ILegacyTextCodec codec, ILogger<ServerCatalog> logger, string? directory = null)
    {
        _codec = codec;
        _logger = logger;
        Directory = directory ?? AppContext.BaseDirectory;
    }

    /// <summary>Where this catalog reads from.</summary>
    public string Directory { get; }

    /// <summary>
    /// Loads the servers and the switches that go with them.
    /// </summary>
    /// <remarks>
    /// A missing server list is reported rather than papered over with an empty one: the
    /// launcher has nothing to connect to without it, and an empty list looks to the
    /// player like the operator has taken every server down.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The server list is not there.</exception>
    /// <exception cref="InvalidDataException">A file is there but could not be read.</exception>
    public ListFile Load()
    {
        var servers = ReadOrThrow(ServerListFileName);

        // Settings from their own file first, then from whatever the server list carries.
        // The other way round would silently ignore a newer package's config.
        var settings = TryRead(SettingsFileName) ?? servers;

        try
        {
            var configured = ListFileCodec.Parse(settings);

            var list = new ListFile
            {
                Servers = [.. ListFileCodec.ParseServers(servers)],
                Aux = configured.Aux,
                Launcher = configured.Launcher,
            };

            _logger.LogInformation("Loaded {Count} servers from {File}", list.Servers.Count, ServerListFileName);
            return list;
        }
        catch (FormatException e)
        {
            throw new InvalidDataException($"{ServerListFileName} 格式不對，讀不出來。", e);
        }
    }

    private string ReadOrThrow(string fileName)
    {
        var path = Path.Combine(Directory, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{Directory} 裡沒有 {fileName}。", path);
        }

        return Decrypt(path, fileName);
    }

    private string? TryRead(string fileName)
    {
        var path = Path.Combine(Directory, fileName);

        return File.Exists(path) ? Decrypt(path, fileName) : null;
    }

    private string Decrypt(string path, string fileName)
    {
        var raw = _codec.ReadTextFile(path);

        try
        {
            return ConfigCipher.DecryptText(raw);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException($"{fileName} 既不是加密設定檔，也不是純文字。", e);
        }
    }
}
