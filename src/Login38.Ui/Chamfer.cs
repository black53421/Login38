using System.Windows;
using System.Windows.Media;

namespace Login38.Ui;

/// <summary>Which corners a chamfer cuts.</summary>
public enum ChamferCorners
{
    /// <summary>None, which is the same as not using it.</summary>
    None,

    /// <summary>Top left and bottom right, which reads as a direction of travel.</summary>
    Diagonal,

    /// <summary>Both corners on the right, for something that ends a row.</summary>
    Trailing,

    /// <summary>Both corners on the left, for something that begins one.</summary>
    Leading,

    /// <summary>All four.</summary>
    All,
}

/// <summary>
/// Cuts the corners off an element.
/// </summary>
/// <remarks>
/// <para>
/// The one shape in this interface that is not a rectangle, and the reason it is not
/// drawn as a rounded corner: a chamfer is what separates a panel meant to look like an
/// instrument from one meant to look like a document. Applied to the few elements that
/// carry weight — the button that starts the game, the strip marking the selected page —
/// rather than to everything, which would be a texture rather than an emphasis.
/// </para>
/// <para>
/// A clip rather than a border template, so it composes with whatever is inside: an
/// element keeps its own background, gradient and content and simply ends early at the
/// corner. The cost is that a clipped border draws no stroke along the cut, so anything
/// needing a visible outline draws it as a sibling <see cref="System.Windows.Shapes.Path"/>
/// rather than relying on <c>BorderBrush</c>.
/// </para>
/// <para>
/// Recomputed on every size change because a clip is in device-independent pixels and does
/// not scale with its element. That is one geometry per resize of a handful of elements,
/// which is nothing; binding it to a converter would cost the same and be harder to read.
/// </para>
/// </remarks>
public static class Chamfer
{
    /// <summary>How far the cut reaches along each edge, in pixels. Zero is off.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size",
        typeof(double),
        typeof(Chamfer),
        new PropertyMetadata(0d, OnChanged));

    /// <summary>Which corners are cut.</summary>
    public static readonly DependencyProperty CornersProperty = DependencyProperty.RegisterAttached(
        "Corners",
        typeof(ChamferCorners),
        typeof(Chamfer),
        new PropertyMetadata(ChamferCorners.Diagonal, OnChanged));

    public static void SetSize(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(SizeProperty, value);
    }

    public static double GetSize(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (double)element.GetValue(SizeProperty);
    }

    public static void SetCorners(DependencyObject element, ChamferCorners value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(CornersProperty, value);
    }

    public static ChamferCorners GetCorners(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (ChamferCorners)element.GetValue(CornersProperty);
    }

    /// <summary>
    /// The outline of a chamfered rectangle.
    /// </summary>
    /// <remarks>
    /// Separated from the clipping so that the shape can also be stroked. Returns null
    /// when there is nothing to cut, which the caller reads as "leave it rectangular"
    /// rather than as a failure.
    /// </remarks>
    /// <param name="size">Width and height of the element.</param>
    /// <param name="cut">How far the cut reaches along each edge.</param>
    /// <param name="corners">Which corners to cut.</param>
    public static Geometry? Outline(Size size, double cut, ChamferCorners corners)
    {
        // A cut wider than half the element would meet itself in the middle and turn the
        // shape into a triangle or an hourglass. Bounded rather than refused: an element
        // is briefly very small while a window is being laid out, and a shape that throws
        // at that moment takes the window with it.
        cut = Math.Min(cut, Math.Min(size.Width, size.Height) / 2);

        if (cut <= 0 || corners == ChamferCorners.None ||
            size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var topLeft = corners is ChamferCorners.Diagonal or ChamferCorners.Leading or ChamferCorners.All;
        var topRight = corners is ChamferCorners.Trailing or ChamferCorners.All;
        var bottomRight = corners is ChamferCorners.Diagonal or ChamferCorners.Trailing or ChamferCorners.All;
        var bottomLeft = corners is ChamferCorners.Leading or ChamferCorners.All;

        var w = size.Width;
        var h = size.Height;
        var figure = new PathFigure { StartPoint = new Point(topLeft ? cut : 0, 0), IsClosed = true };

        Line(figure, topRight ? w - cut : w, 0);

        if (topRight)
        {
            Line(figure, w, cut);
        }

        Line(figure, w, bottomRight ? h - cut : h);

        if (bottomRight)
        {
            Line(figure, w - cut, h);
        }

        Line(figure, bottomLeft ? cut : 0, h);

        if (bottomLeft)
        {
            Line(figure, 0, h - cut);
        }

        // Only when the corner it leads to is cut. An uncut top left is where the walk
        // started, and closing the figure gets back there on its own — emitting the line
        // as well would leave the start point in the outline twice.
        if (topLeft)
        {
            Line(figure, 0, cut);
        }

        var geometry = new PathGeometry();

        geometry.Figures.Add(figure);
        geometry.Freeze();

        return geometry;
    }

    private static void Line(PathFigure figure, double x, double y) =>
        figure.Segments.Add(new LineSegment(new Point(x, y), isStroked: true));

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement target)
        {
            return;
        }

        // Subscribed once however many of the two properties are set, and never
        // unsubscribed: the handler outlives nothing, since it is the element's own.
        target.SizeChanged -= OnResized;
        target.SizeChanged += OnResized;

        // And on load. A template trigger sets this while the element still measures zero,
        // and an element whose size never changes after that — one inside a panel that
        // gives it exactly what it asked for — would keep the empty clip it was given.
        target.Loaded -= OnLoaded;
        target.Loaded += OnLoaded;

        Apply(target);
    }

    private static void OnResized(object sender, SizeChangedEventArgs e) => Apply((FrameworkElement)sender);

    private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement target) =>
        target.Clip = Outline(target.RenderSize, GetSize(target), GetCorners(target));
}
