using Login38.Encoder.Services;

namespace Login38.Encoder.Tests;

/// <summary>
/// Answers the encoder's questions without a desktop.
/// </summary>
/// <remarks>
/// Every dialog in the reference was opened from inside the method that did the work, so
/// none of that work could be run except by hand. Behind an interface, the same methods
/// are ordinary code with an argument.
/// </remarks>
public sealed class StubPrompts : IFilePrompts
{
    /// <summary>What the next single-file question is answered with.</summary>
    public string? File { get; set; }

    /// <summary>And the next several-files one.</summary>
    public IReadOnlyList<string> Files { get; set; } = [];

    /// <summary>And the next folder one.</summary>
    public string? Folder { get; set; }

    /// <summary>Everything the encoder said.</summary>
    public List<string> Said { get; } = [];

    /// <inheritdoc/>
    public string? OpenFile(string title, string filter) => File;

    /// <inheritdoc/>
    public IReadOnlyList<string> OpenFiles(string title, string filter) => Files;

    /// <inheritdoc/>
    public string? OpenFolder(string title) => Folder;

    /// <inheritdoc/>
    public void Say(string message) => Said.Add(message);
}
