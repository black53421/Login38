namespace Login38.Encoder.Services;

/// <summary>
/// Asking the operator for a file, a folder, or an acknowledgement.
/// </summary>
/// <remarks>
/// Behind an interface so that what the encoder does with the answers can be exercised
/// without a desktop. Every one of these is a modal dialog, and the reference had them
/// woven through the same methods that read files and built packages — so none of that
/// could be run except by hand.
/// </remarks>
public interface IFilePrompts
{
    /// <summary>Asks for one existing file, or null when the operator changed their mind.</summary>
    /// <param name="filter">In the Win32 form: <c>Description|*.ext|…</c>.</param>
    string? OpenFile(string title, string filter);

    /// <summary>Asks for several, in the order the operator picked them.</summary>
    IReadOnlyList<string> OpenFiles(string title, string filter);

    /// <summary>Asks for a folder, or null.</summary>
    string? OpenFolder(string title);

    /// <summary>Tells the operator something they have to see.</summary>
    void Say(string message);
}
