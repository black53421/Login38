using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Login38.Core.Icons;

/// <summary>
/// <c>dynamicicons.xml</c> — which item icons animate, and how.
/// </summary>
/// <remarks>
/// <para>
/// Operators write this by hand and ship it inside the icon package. It is read once at
/// launch, so everything wrong with it should be said then, with enough detail to fix it —
/// the alternative is an icon that quietly does not animate, or a client that exits with a
/// divide error the first time the item appears on screen.
/// </para>
/// <para>
/// The reference scanned for <c>&lt;item</c> as a substring, which also matches
/// <c>&lt;items</c>, read attributes by searching anywhere inside the tag, and truncated
/// every number to the width of the field it landed in. A real parser costs nothing here
/// and reports the line.
/// </para>
/// </remarks>
public static class DynamicIconManifest
{
    /// <summary>The file, as it is named inside the package.</summary>
    public const string FileName = "dynamicicons.xml";

    private const string Root = "dynamicicons";
    private const string Item = "item";
    private const string Frame = "png";
    private const string IconAttribute = "tbt";
    private const string SpeedAttribute = "speed";
    private const string RestAttribute = "interval";

    /// <summary>Reads a manifest, in order of icon number.</summary>
    /// <exception cref="DynamicIconException">
    /// The document is not well-formed, or an entry could not be used.
    /// </exception>
    public static IReadOnlyList<IconAnimation> Parse(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            throw new DynamicIconException($"{FileName} is not valid XML: {e.Message}", e);
        }

        if (document.Root is not { } root || root.Name != Root)
        {
            throw new DynamicIconException(
                $"{FileName} should have a <{Root}> element at its root.");
        }

        var animations = new List<IconAnimation>();
        var seen = new HashSet<ushort>();

        foreach (var element in root.Elements(Item))
        {
            var animation = ReadItem(element);

            // Two entries for one icon is a mistake with no sensible reading. The reference
            // kept whichever came last and said nothing, so an operator who duplicated an
            // entry while editing saw the wrong animation and no reason for it.
            if (!seen.Add(animation.Icon))
            {
                throw new DynamicIconException(
                    $"{Where(element)}icon {animation.Icon} has more than one <{Item}> entry.");
            }

            animations.Add(animation);
        }

        animations.Sort((left, right) => left.Icon.CompareTo(right.Icon));
        return animations;
    }

    /// <summary>Writes a manifest that <see cref="Parse"/> reads back unchanged.</summary>
    public static string ToXml(IEnumerable<IconAnimation> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);

        var text = new StringBuilder();
        text.Append('<').Append(Root).Append(">\n");

        foreach (var animation in animations.OrderBy(a => a.Icon))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  <{Item} {IconAttribute}=\"{animation.Icon}\" {SpeedAttribute}=\"{animation.FrameMilliseconds}\" {RestAttribute}=\"{animation.RestMilliseconds}\">\n");

            foreach (var frame in animation.Frames)
            {
                text.Append(CultureInfo.InvariantCulture, $"    <{Frame}>{frame}</{Frame}>\n");
            }

            text.Append(CultureInfo.InvariantCulture, $"  </{Item}>\n");
        }

        return text.Append("</").Append(Root).Append(">\n").ToString();
    }

    private static IconAnimation ReadItem(XElement element)
    {
        var icon = ReadNumber(element, IconAttribute, ushort.MaxValue);

        // The frame time divides the clock inside the game. Zero would make the animation
        // occupy no time at all, and with no rest either the whole cycle is zero — which
        // the client meets as a division by zero rather than as a missing animation.
        var speed = ReadNumber(element, SpeedAttribute, ushort.MaxValue);

        if (speed == 0)
        {
            throw new DynamicIconException(
                $"{Where(element)}icon {icon} has {SpeedAttribute}=\"0\"; a frame has to be shown for at least a millisecond.");
        }

        var rest = ReadNumber(element, RestAttribute, uint.MaxValue);

        var frames = new List<uint>();

        foreach (var frame in element.Elements(Frame))
        {
            var text = frame.Value.Trim();

            if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                throw new DynamicIconException(
                    $"{Where(frame)}icon {icon} has a <{Frame}> of \"{text}\", which is not a resource number.");
            }

            frames.Add(id);
        }

        if (frames.Count is 0 or > IconAnimation.MaxFrames)
        {
            throw new DynamicIconException(
                $"{Where(element)}icon {icon} has {frames.Count} frames; it needs between 1 and {IconAnimation.MaxFrames}.");
        }

        return new IconAnimation((ushort)icon, (ushort)speed, rest, frames);
    }

    private static uint ReadNumber(XElement element, string name, uint limit)
    {
        if (element.Attribute(name) is not { } attribute)
        {
            throw new DynamicIconException($"{Where(element)}<{Item}> has no {name}.");
        }

        var text = attribute.Value.Trim();

        // Range-checked rather than truncated. The reference cast to the field's width, so
        // tbt="70000" animated icon 4464 — an icon the operator never mentioned.
        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value > limit)
        {
            throw new DynamicIconException(
                $"{Where(element)}{name}=\"{text}\" is not a number between 0 and {limit}.");
        }

        return value;
    }

    private static string Where(XObject node) =>
        node is IXmlLineInfo line && line.HasLineInfo()
            ? string.Create(CultureInfo.InvariantCulture, $"{FileName} line {line.LineNumber}: ")
            : $"{FileName}: ";
}
