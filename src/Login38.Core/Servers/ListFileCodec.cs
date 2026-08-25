using System.Globalization;
using System.Text;
using Login38.Core.Configuration;
using Login38.Core.Text;

namespace Login38.Core.Servers;

/// <summary>
/// Reads and writes the operator-distributed config files.
/// </summary>
/// <remarks>
/// Parsing is forgiving throughout: unknown sections, unknown keys, malformed numbers
/// and undecodable server entries are skipped rather than rejected. These files reach
/// players through several generations of tooling, and a single stale key must not
/// stop the game from starting.
/// </remarks>
public static class ListFileCodec
{
    private const string ListSection = "list";
    private const string AuxSection = "aux";
    private const string LauncherSection = "launcher";
    private const string ServerDataPrefix = "serverdata";

    /// <summary>Parses a combined or partial config document.</summary>
    public static ListFile Parse(string content)
    {
        var servers = new List<ServerInfo>();
        var aux = new AuxConfig();
        var launcher = new LauncherConfig();

        foreach (var entry in IniReader.Read(content))
        {
            switch (entry.Section)
            {
                case ListSection:
                    if (TryParseServer(entry) is { } server)
                    {
                        servers.Add(server);
                    }

                    break;

                case AuxSection:
                    ApplyAux(aux, entry.Key, entry.Value);
                    break;

                case LauncherSection:
                    ApplyLauncher(launcher, entry.Key, entry.Value);
                    break;
            }
        }

        return new ListFile
        {
            Servers = servers,
            Aux = aux.Normalized(),
            Launcher = launcher,
        };
    }

    /// <summary>Parses just the server list.</summary>
    public static IReadOnlyList<ServerInfo> ParseServers(string content) =>
        (IReadOnlyList<ServerInfo>)Parse(content).Servers.AsReadOnly();

    /// <summary>
    /// Builds <c>list.txt</c>: the plain <c>[list]</c> format, which the stock client
    /// can also read.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="ListFile.MaxServers"/> entries.</exception>
    public static string BuildServerList(IReadOnlyList<ServerInfo> servers)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(servers.Count, ListFile.MaxServers);

        var output = new StringBuilder("[list]\n");
        for (var i = 0; i < servers.Count; i++)
        {
            var buffer = servers[i].ToBytes();
            ConfigCipher.Encrypt(ConfigCipher.FileKey, buffer);
            output.Append(CultureInfo.InvariantCulture, $"ServerData{i}={Convert.ToBase64String(buffer)}\n");
        }

        return output.ToString();
    }

    /// <summary>
    /// Builds <c>config.ini</c>: the <c>[aux]</c> and <c>[launcher]</c> sections. The
    /// server list is written separately by <see cref="BuildServerList"/>.
    /// </summary>
    public static string BuildConfig(ListFile file)
    {
        var aux = file.Aux.Normalized();
        var launcher = file.Launcher;
        var output = new StringBuilder();

        output.Append("[aux]\n");
        AppendBool(output, "packet_encrypt", aux.PacketEncrypt);
        AppendBool(output, "anti_cheat_basic", aux.AntiCheatBasic);
        AppendBool(output, "anti_cheat_advanced", aux.AntiCheatAdvanced);
        AppendBool(output, "transform_file", aux.TransformFile);
        AppendBool(output, "multi_instance", aux.MultiInstance);
        AppendUInt(output, "multi_instance_limit", aux.MultiInstanceLimit);
        AppendBool(output, "move_packet_no_encrypt", aux.MovePacketNoEncrypt);
        AppendBool(output, "lhx_aux_enabled", aux.LhxAuxEnabled);
        AppendBool(output, "hp_mp_limit_enabled", aux.HpMpLimitEnabled);
        AppendBool(output, "ac_mr_limit_enabled", aux.AcMrLimitEnabled);
        AppendBool(output, "inventory_limit_enabled", aux.InventoryLimitEnabled);
        AppendBool(output, "equip_ui_enabled", aux.EquipUiEnabled);
        AppendBool(output, "img_limit_enabled", aux.ImgLimitEnabled);
        AppendUInt(output, "inventory_limit_value", aux.InventoryLimitValue);
        AppendUInt(output, "img_limit_value", aux.ImgLimitValue);
        AppendText(output, "text_encoding", aux.TextEncoding.ToConfigValue());
        AppendBool(output, "dynamic_dialog_enabled", aux.DynamicDialogEnabled);
        AppendBool(output, "pickup_toast_enabled", aux.PickupToastEnabled);
        AppendBool(output, "exp_drift_enabled", aux.ExpDriftEnabled);
        AppendBool(output, "internal_bot_enabled", aux.InternalBotEnabled);
        AppendBool(output, "dynamic_icon_enabled", aux.DynamicIconEnabled);
        AppendText(output, "dynamic_icon_pak_name", aux.DynamicIconPakName);
        AppendText(output, "transform_file_name", aux.TransformFileName);

        output.Append("\n[launcher]\n");
        AppendText(output, "active_skin", launcher.ActiveSkin);
        AppendBool(output, "announcement_enabled", launcher.AnnouncementEnabled);
        AppendText(output, "announcement_url", launcher.AnnouncementUrl);
        AppendBool(output, "list_update_enabled", launcher.ListUpdateEnabled);
        AppendText(output, "list_update_url", launcher.ListUpdateUrl);
        AppendBool(output, "auto_update_enabled", launcher.AutoUpdateEnabled);
        AppendText(output, "auto_update_url", launcher.AutoUpdateUrl);
        AppendText(output, "official_url", launcher.OfficialUrl);
        AppendText(output, "customer_service_url", launcher.CustomerServiceUrl);

        return output.ToString();
    }

    private static ServerInfo? TryParseServer(IniEntry entry)
    {
        if (!entry.Key.StartsWith(ServerDataPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(entry.Value);
        }
        catch (FormatException)
        {
            return null;
        }

        if (buffer.Length < ServerInfo.SerializedSize)
        {
            return null;
        }

        ConfigCipher.Decrypt(ConfigCipher.FileKey, buffer);
        return ServerInfo.Parse(buffer);
    }

    private static void ApplyAux(AuxConfig aux, string key, string value)
    {
        switch (key)
        {
            case "packet_encrypt": aux.PacketEncrypt = BooleanText.IsTruthy(value); break;
            case "anti_cheat_basic": aux.AntiCheatBasic = BooleanText.IsTruthy(value); break;
            case "anti_cheat_advanced": aux.AntiCheatAdvanced = BooleanText.IsTruthy(value); break;
            case "transform_file": aux.TransformFile = BooleanText.IsTruthy(value); break;
            case "multi_instance": aux.MultiInstance = BooleanText.IsTruthy(value); break;
            case "move_packet_no_encrypt": aux.MovePacketNoEncrypt = BooleanText.IsTruthy(value); break;
            case "lhx_aux_enabled": aux.LhxAuxEnabled = BooleanText.IsTruthy(value); break;
            case "hp_mp_limit_enabled": aux.HpMpLimitEnabled = BooleanText.IsTruthy(value); break;
            case "ac_mr_limit_enabled": aux.AcMrLimitEnabled = BooleanText.IsTruthy(value); break;
            case "inventory_limit_enabled": aux.InventoryLimitEnabled = BooleanText.IsTruthy(value); break;
            case "equip_ui_enabled": aux.EquipUiEnabled = BooleanText.IsTruthy(value); break;
            case "img_limit_enabled": aux.ImgLimitEnabled = BooleanText.IsTruthy(value); break;
            case "dynamic_dialog_enabled": aux.DynamicDialogEnabled = BooleanText.IsTruthy(value); break;
            case "dynamic_icon_enabled": aux.DynamicIconEnabled = BooleanText.IsTruthy(value); break;
            case "pickup_toast_enabled": aux.PickupToastEnabled = BooleanText.IsTruthy(value); break;
            case "exp_drift_enabled": aux.ExpDriftEnabled = BooleanText.IsTruthy(value); break;
            case "internal_bot_enabled": aux.InternalBotEnabled = BooleanText.IsTruthy(value); break;

            case "multi_instance_limit":
                aux.MultiInstanceLimit = ParseClamped(
                    value, AuxConfig.MultiInstanceLimitBounds.Default, AuxConfig.MultiInstanceLimitBounds.Min, AuxConfig.MultiInstanceLimitBounds.Max);
                break;

            case "inventory_limit_value":
                aux.InventoryLimitValue = ParseClamped(
                    value, AuxConfig.InventoryLimitBounds.Default, AuxConfig.InventoryLimitBounds.Min, AuxConfig.InventoryLimitBounds.Max);
                break;

            case "img_limit_value":
                aux.ImgLimitValue = ParseClamped(
                    value, AuxConfig.ImgLimitBounds.Default, AuxConfig.ImgLimitBounds.Min, AuxConfig.ImgLimitBounds.Max);
                break;

            case "text_encoding":
                aux.TextEncoding = value.ToTextEncodingMode();
                break;

            case "dynamic_icon_pak_name":
                // An empty value means "unset", not "no pak"; keep the default.
                if (!string.IsNullOrWhiteSpace(value))
                {
                    aux.DynamicIconPakName = value.Trim();
                }

                break;

            // Empty here does mean something: the table named after the client. Unlike
            // the icon pak there is a sensible file to fall back to, so an operator who
            // clears the box gets it rather than getting nothing.
            case "transform_file_name":
                aux.TransformFileName = value.Trim();
                break;
        }
    }

    private static void ApplyLauncher(LauncherConfig launcher, string key, string value)
    {
        switch (key)
        {
            case "active_skin": launcher.ActiveSkin = value; break;
            case "announcement_enabled": launcher.AnnouncementEnabled = BooleanText.IsTruthy(value); break;
            case "announcement_url": launcher.AnnouncementUrl = value; break;
            case "list_update_enabled": launcher.ListUpdateEnabled = BooleanText.IsTruthy(value); break;
            case "list_update_url": launcher.ListUpdateUrl = value; break;
            case "auto_update_enabled": launcher.AutoUpdateEnabled = BooleanText.IsTruthy(value); break;
            case "auto_update_url": launcher.AutoUpdateUrl = value; break;
            case "official_url": launcher.OfficialUrl = value; break;
            case "customer_service_url": launcher.CustomerServiceUrl = value; break;
        }
    }

    /// <summary>
    /// Parses a bounded number. An out-of-range value is clamped, but an unparseable
    /// one falls back to the default rather than to the nearest bound — garbage should
    /// not silently become a valid extreme.
    /// </summary>
    private static uint ParseClamped(string value, uint fallback, uint min, uint max) =>
        uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

    private static void AppendBool(StringBuilder output, string key, bool value) =>
        output.Append(key).Append('=').Append(BooleanText.ToConfigValue(value)).Append('\n');

    private static void AppendUInt(StringBuilder output, string key, uint value) =>
        output.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void AppendText(StringBuilder output, string key, string value) =>
        output.Append(key).Append('=').Append(value).Append('\n');
}
