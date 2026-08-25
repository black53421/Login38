using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Core.Servers;

namespace Login38.App.ViewModels;

/// <summary>Whether a server answered when it was last checked.</summary>
public enum ServerAvailability
{
    /// <summary>Not checked yet.</summary>
    Unknown,

    Online,

    Offline,
}

/// <summary>One server in the list the player chooses from.</summary>
public sealed partial class ServerEntryViewModel : ObservableObject
{
    public ServerEntryViewModel(ServerInfo server, int index)
    {
        ArgumentNullException.ThrowIfNull(server);

        Server = server;
        Index = index;
    }

    /// <summary>The entry as it came out of the operator's list.</summary>
    public ServerInfo Server { get; }

    /// <summary>Position in the list, which is also how the operator ordered them.</summary>
    public int Index { get; }

    public string Name => Server.Name;

    /// <summary>
    /// Whether the operator marked this entry as one to offer.
    /// </summary>
    /// <remarks>
    /// The list has a fixed number of slots, and unused ones are still present with empty
    /// contents. Offering them would give the player rows that cannot work.
    /// </remarks>
    public bool IsOffered => Server.InUse && !string.IsNullOrWhiteSpace(Server.IpAddress);

    [ObservableProperty]
    private ServerAvailability _availability = ServerAvailability.Unknown;
}
