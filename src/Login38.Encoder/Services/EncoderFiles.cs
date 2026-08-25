using System.IO;
using System.Text;
using Login38.Core.Configuration;
using Login38.Core.Cryptography;
using Login38.Core.Servers;
using Login38.Core.Text;

namespace Login38.Encoder.Services;

/// <summary>What was read from beside the encoder, and anything that could not be.</summary>
/// <param name="File">The servers and the settings, defaulted where they were missing.</param>
/// <param name="Problems">
/// What could not be read, in the operator's words. A file that is unreadable is not a
/// reason to refuse to start — it is a reason to say so and carry on with defaults, because
/// the next thing the operator does is write a good one over it.
/// </param>
public readonly record struct EncoderLoad(ListFile File, IReadOnlyList<string> Problems);

/// <summary>
/// The two files the encoder writes, beside itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>list.txt</c> carries the servers and <c>config.ini</c> carries everything else. They
/// are separate because the stock client reads the first one and knows nothing about the
/// second, so the launcher's own settings had to go somewhere it would not choke on.
/// </para>
/// <para>
/// Beside the executable rather than under the user's profile, because an operator zips
/// this directory up and gives it to their players.
/// </para>
/// </remarks>
public sealed class EncoderFiles
{
    /// <summary>The servers, in the format the stock client also reads.</summary>
    public const string ServerListName = "list.txt";

    /// <summary>Everything else, encrypted.</summary>
    public const string ConfigName = "config.ini";

    /// <summary>What the server end is given, for the key that binds a client to it.</summary>
    public const string PackPropertiesName = "pack.properties";

    private readonly ILegacyTextCodec _codec;

    /// <param name="directory">Where the files live. Defaults to beside the encoder.</param>
    public EncoderFiles(ILegacyTextCodec codec, string? directory = null)
    {
        _codec = codec;
        Directory = directory ?? AppContext.BaseDirectory;
    }

    /// <summary>Where the files live.</summary>
    public string Directory { get; }

    /// <summary>Where one of them is.</summary>
    public string PathFor(string name) => Path.Combine(Directory, name);

    /// <summary>
    /// Reads both files.
    /// </summary>
    /// <remarks>
    /// Through the codec rather than as UTF-8. These are files operators hand-edit and hand
    /// around, and a code page 950 one is common enough that treating it as unreadable
    /// would lose somebody's server list.
    /// </remarks>
    public EncoderLoad Load()
    {
        var problems = new List<string>();
        var servers = new List<ServerInfo>();
        var aux = new AuxConfig();
        var launcher = new LauncherConfig();

        if (Text(ServerListName, problems) is { } list)
        {
            try
            {
                servers.AddRange(ListFileCodec.ParseServers(list));
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                problems.Add($"{ServerListName} 讀不出來:{e.Message}");
            }
        }

        if (Text(ConfigName, problems) is { } config)
        {
            try
            {
                var file = ListFileCodec.Parse(ConfigCipher.DecryptText(config));

                aux = file.Aux;
                launcher = file.Launcher;

                // Older installations kept the servers in here too. Taking them only when
                // there is no list of its own is what moves an operator forward without
                // overwriting the newer file.
                if (servers.Count == 0)
                {
                    servers.AddRange(file.Servers);
                }
            }
            catch (Exception e) when (e is FormatException or ArgumentException or InvalidDataException)
            {
                problems.Add($"{ConfigName} 讀不出來:{e.Message}");
            }
        }

        return new EncoderLoad(
            new ListFile { Servers = servers, Aux = aux, Launcher = launcher }, problems);
    }

    /// <summary>
    /// Writes both files.
    /// </summary>
    /// <remarks>
    /// Every slot with a name in it, in the order the operator arranged them. The reference
    /// wrote one slot at a time, merging it into whatever was on disk by position — and
    /// then dropped the unnamed slots on the way out, so every gap moved everything after
    /// it up one and the slot an operator was editing was not the slot they got.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">More slots than the format holds.</exception>
    public void Save(ListFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var named = file.Servers.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();

        Write(ServerListName, ListFileCodec.BuildServerList(named));
        Write(ConfigName, ConfigCipher.EncryptText(ListFileCodec.BuildConfig(
            new ListFile { Servers = [], Aux = file.Aux, Launcher = file.Launcher })));
    }

    /// <summary>
    /// Writes the key file the server end reads.
    /// </summary>
    /// <returns>Where it was written.</returns>
    /// <remarks>
    /// The private exponent goes in as well, commented as a spare. It is already in the
    /// client's own list, so this file is not what makes it known — but an operator who
    /// loses <c>list.txt</c> and has this can put the pair back together.
    /// </remarks>
    public string WritePackProperties(Rsa32Key key)
    {
        var path = PathFor(PackPropertiesName);

        Write(PackPropertiesName, string.Join('\n',
        [
            "; 由編碼器產生 — 綁定客戶端與這台伺服器的金鑰。",
            "; 請放到伺服器的 ./config/ 目錄。",
            "; 客戶端那一半已經寫在 list.txt,這裡的 D 只是備份。",
            "Autoentication=True",
            $"RSA_KEY_E={key.E}",
            $"RSA_KEY_D={key.D}",
            $"RSA_KEY_N={key.N}",
            string.Empty,
        ]));

        return path;
    }

    private string? Text(string name, List<string> problems)
    {
        var path = PathFor(name);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return _codec.ReadTextFile(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{name} 開不起來:{e.Message}");

            return null;
        }
    }

    /// <summary>
    /// Writes one file as UTF-8 with no byte order mark.
    /// </summary>
    /// <remarks>
    /// No mark because the client reads <c>list.txt</c> too, and it does not know what one
    /// is: three bytes in front of <c>[list]</c> and it finds no section at all.
    /// </remarks>
    private void Write(string name, string content)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(name), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
