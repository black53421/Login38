using System.Windows;
using System.Windows.Media;
using Login38.Ui;
using Shouldly;

namespace Login38.App.Tests.Views;

/// <summary>
/// The cut corner, which is the one shape in the interface that is not a rectangle.
/// </summary>
/// <remarks>
/// Worth pinning because it is arithmetic that runs during layout, when an element is
/// briefly a size nobody intended. A geometry that threw at that moment would take the
/// window with it, and one that came back wrong would be a shape nobody could name.
/// </remarks>
public sealed class ChamferTests
{
    [Fact]
    public void CutsTheTwoCornersThatMarkADirection()
    {
        var outline = Points(new Size(100, 40), 10, ChamferCorners.Diagonal);

        // Six corners rather than four: each cut turns one into two.
        outline.Count.ShouldBe(6);
        outline.ShouldContain(new Point(10, 0));
        outline.ShouldContain(new Point(0, 10));
        outline.ShouldContain(new Point(90, 40));
        outline.ShouldContain(new Point(100, 30));

        // Closed rather than walked back to where it began, so no corner is listed twice.
        outline.Distinct().Count().ShouldBe(outline.Count);
    }

    [Fact]
    public void CutsAllFourWhenAskedTo() =>
        Points(new Size(100, 40), 10, ChamferCorners.All).Count.ShouldBe(8);

    [Theory]
    [InlineData(ChamferCorners.Leading)]
    [InlineData(ChamferCorners.Trailing)]
    public void CutsOneEndOrTheOther(ChamferCorners corners) =>
        Points(new Size(100, 40), 10, corners).Count.ShouldBe(6);

    [Fact]
    public void LeavesItRectangularWhenThereIsNothingToCut()
    {
        Chamfer.Outline(new Size(100, 40), 0, ChamferCorners.Diagonal).ShouldBeNull();
        Chamfer.Outline(new Size(100, 40), 10, ChamferCorners.None).ShouldBeNull();
    }

    // A window being laid out passes through sizes nobody asked for, and a shape that
    // threw at that moment would take the window down rather than draw nothing.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 40)]
    [InlineData(100, 0)]
    [InlineData(-5, -5)]
    public void HasNothingToDrawForAnElementWithNoSize(double width, double height) =>
        Chamfer.Outline(new Size(Math.Max(width, 0), Math.Max(height, 0)), 10, ChamferCorners.All)
            .ShouldBeNull();

    // A cut wider than half the element would meet itself in the middle and turn a
    // rectangle into an hourglass. Bounded rather than refused.
    // At exactly half the cuts meet and the rectangle becomes a diamond, which is the
    // right answer rather than an error: an element is briefly this small while a window
    // is being laid out.
    [Fact]
    public void NeverCutsPastTheMiddle()
    {
        var outline = Points(new Size(20, 20), 400, ChamferCorners.All);

        outline.ShouldAllBe(p => p.X >= 0 && p.X <= 20 && p.Y >= 0 && p.Y <= 20);
        outline.Distinct().ShouldBe([new Point(10, 0), new Point(20, 10), new Point(10, 20), new Point(0, 10)], ignoreOrder: true);
    }

    [Fact]
    public void ComesBackFrozenSoItCanBeSharedAcrossThreads() =>
        Chamfer.Outline(new Size(100, 40), 10, ChamferCorners.Diagonal)!.IsFrozen.ShouldBeTrue();

    private static List<Point> Points(Size size, double cut, ChamferCorners corners)
    {
        var figure = ((PathGeometry)Chamfer.Outline(size, cut, corners)!).Figures[0];
        var points = new List<Point> { figure.StartPoint };

        points.AddRange(figure.Segments.Cast<LineSegment>().Select(s => s.Point));

        return points;
    }
}
