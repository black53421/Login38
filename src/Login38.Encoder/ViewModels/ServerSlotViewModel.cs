using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Login38.Core.Cryptography;
using Login38.Core.Servers;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// One of the eight servers a launcher can offer.
/// </summary>
/// <remarks>
/// A slot with no name is an empty slot: that is how the format says "there is nothing
/// here", and it is what the launcher's own list reader looks at.
/// </remarks>
public sealed partial class ServerSlotViewModel : ObservableObject
{
    /// <summary>The port a Lineage server listens on unless told otherwise.</summary>
    public const int DefaultPort = 2000;

    /// <param name="slot">Which of the eight this is, counted from zero.</param>
    public ServerSlotViewModel(int slot)
    {
        Slot = slot;
        Name = Placeholder(slot);
    }

    /// <summary>Which of the eight this is.</summary>
    public int Slot { get; }

    /// <summary>What the player sees in the launcher's list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _name;

    [ObservableProperty]
    private string _address = "127.0.0.1";

    [ObservableProperty]
    private double _port = DefaultPort;

    /// <summary>How the slot appears in the list of them.</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{Slot + 1}. (空白)"
            : $"{Slot + 1}. {Name}";

    /// <summary>Whether this slot would be written out.</summary>
    public bool IsUsed => !string.IsNullOrWhiteSpace(Name);

    /// <summary>Fills the slot in from a server.</summary>
    public void Load(ServerInfo server)
    {
        ArgumentNullException.ThrowIfNull(server);

        Name = server.Name;
        Address = server.IpAddress;
        Port = server.Port;
    }

    /// <summary>Puts it back to a usable starting point, for an operator with no file yet.</summary>
    public void Reset()
    {
        Clear();
        Name = Placeholder(Slot);
    }

    /// <summary>Empties it.</summary>
    public void Clear()
    {
        Name = string.Empty;
        Address = "127.0.0.1";
        Port = DefaultPort;
    }

    /// <summary>
    /// Reads it back out, stamped with the key every server in this list shares.
    /// </summary>
    /// <remarks>
    /// The key is a parameter rather than a field because there is one of it. The format
    /// carries a copy per slot — the client reads it out of whichever row the player
    /// picked — but an operator maintains one pair, and a type that could hold eight
    /// different ones is a type that lets seven of them be wrong.
    /// </remarks>
    public ServerInfo ToServer(Rsa32Key key) => new()
    {
        Name = Name.Trim(),
        IpAddress = Address.Trim(),
        Port = (int)Math.Clamp(Math.Round(Port), 1, ushort.MaxValue),
        InUse = IsUsed,
        RsaE = key.E,
        RsaD = key.D,
        RsaN = key.N,
    };

    private static string Placeholder(int slot) =>
        "Server" + (slot + 1).ToString(CultureInfo.InvariantCulture);
}
