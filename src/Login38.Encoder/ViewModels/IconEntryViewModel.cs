using System.Globalization;
using Login38.Core.Icons;

namespace Login38.Encoder.ViewModels;

/// <summary>
/// One icon the client will animate, and the pictures it animates through.
/// </summary>
/// <remarks>
/// The pictures are held as the bytes they were read as, not as decoded images. They go
/// into the package exactly as they came out of the operator's files — the client decodes
/// them, and anything this did to them in between would be a change nobody asked for.
/// </remarks>
public sealed class IconEntryViewModel
{
    public IconEntryViewModel(
        ushort icon, ushort frameMilliseconds, uint restMilliseconds, IReadOnlyList<byte[]> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        Icon = icon;
        FrameMilliseconds = frameMilliseconds;
        RestMilliseconds = restMilliseconds;
        Frames = [.. frames];
    }

    /// <summary>Which of the client's own icons this replaces.</summary>
    public ushort Icon { get; }

    /// <summary>How long each picture is shown.</summary>
    public ushort FrameMilliseconds { get; }

    /// <summary>And how long it waits before starting again.</summary>
    public uint RestMilliseconds { get; }

    /// <summary>The pictures, in the order they play.</summary>
    public IReadOnlyList<byte[]> Frames { get; }

    /// <summary>How the entry appears in the list of them.</summary>
    public string Label => string.Create(CultureInfo.CurrentCulture,
        $"gfxid={Icon} 速度={FrameMilliseconds}ms 間隔={RestMilliseconds}ms 幀數={Frames.Count}");

    /// <summary>Turns it into a manifest entry, given where its pictures ended up.</summary>
    public IconAnimation ToAnimation(IReadOnlyList<uint> frameIds) =>
        new(Icon, FrameMilliseconds, RestMilliseconds, frameIds);
}
