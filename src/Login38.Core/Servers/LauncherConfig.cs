namespace Login38.Core.Servers;

/// <summary>
/// Launcher appearance and update endpoints, from the <c>[launcher]</c> section.
/// </summary>
/// <remarks>
/// These belong to the server operator, not the player. Player preferences such as
/// windowed mode live in <c>launcher.ini</c> instead, so that redistributing a config
/// file never overwrites someone's own settings.
/// <para>
/// The defaults match the reference build's example values.
/// </para>
/// </remarks>
public sealed class LauncherConfig
{
    /// <summary>Name of the active skin folder next to the executable.</summary>
    public string ActiveSkin { get; set; } = "default";

    /// <summary>Whether to show the announcement page.</summary>
    public bool AnnouncementEnabled { get; set; } = true;

    public string AnnouncementUrl { get; set; } =
        "http://tw.beanfun.com/lineage/patch/main_defaultTemplate.asp";

    /// <summary>Whether to refresh the server list from <see cref="ListUpdateUrl"/> at startup.</summary>
    public bool ListUpdateEnabled { get; set; }

    public string ListUpdateUrl { get; set; } = "http://dl.dropbox.com/u/5114050/Login.ini";

    /// <summary>Whether to check for a newer launcher build at startup.</summary>
    public bool AutoUpdateEnabled { get; set; }

    public string AutoUpdateUrl { get; set; } =
        "http://dl.dropbox.com/u/5114050/LoginUpdate/Update.ini";

    /// <summary>Target of the "official site" link. Empty hides the link.</summary>
    public string OfficialUrl { get; set; } = "http://tw.beanfun.com/lineage/";

    /// <summary>Target of the "support" link. Empty hides the link.</summary>
    public string CustomerServiceUrl { get; set; } = "http://tw.beanfun.com/beanfun_help/";
}
