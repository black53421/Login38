namespace Login38.Core.Icons;

/// <summary>An icon package the operator built that cannot be used as it stands.</summary>
/// <remarks>
/// Everything this covers — the manifest, the package index, the frame images — is written
/// by an operator and read once at launch. The message is the only diagnosis they will get,
/// so it names the file and, where there is one, the line.
/// </remarks>
public sealed class DynamicIconException : Exception
{
    public DynamicIconException(string message) : base(message)
    {
    }

    public DynamicIconException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DynamicIconException()
    {
    }
}
