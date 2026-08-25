using Login38.Aux.Notifications;
using Shouldly;

namespace Login38.Aux.Tests.Notifications;

/// <summary>
/// Covers recovering transparency from the client's artwork.
/// </summary>
public sealed class SpriteImageTests
{
    // Over black a pixel is colour × alpha; over white it is that plus (1 - alpha). The
    // difference is 1 - alpha whatever the colour was.
    [Fact]
    public void RecoversAHalfTransparentPixelFromThePair()
    {
        var black = Solid(2, 2, 64, 64, 64);
        var white = Solid(2, 2, 191, 191, 191);

        var paired = SpriteImage.Paired(black, white)!;

        paired.Rgba[3].ShouldBeInRange((byte)126, (byte)129);
        paired.Rgba[0].ShouldBeInRange((byte)126, (byte)130);
    }

    [Fact]
    public void LeavesASolidPixelSolid()
    {
        var paired = SpriteImage.Paired(Solid(1, 1, 200, 100, 50), Solid(1, 1, 200, 100, 50))!;

        paired.Rgba.ShouldBe([200, 100, 50, 255]);
    }

    [Fact]
    public void MakesAPixelThatIsWhateverIsBehindItTransparent()
    {
        var paired = SpriteImage.Paired(Solid(1, 1, 0, 0, 0), Solid(1, 1, 255, 255, 255))!;

        paired.Rgba.ShouldBe([0, 0, 0, 0]);
    }

    // Each channel gives its own answer and the least transparent wins, so an edge pixel is
    // not read as solid because one channel happened to agree in both copies.
    [Fact]
    public void TakesTheLeastTransparentChannel()
    {
        var black = Solid(1, 1, 0, 0, 0);
        var white = Solid(1, 1, 0, 128, 255);

        SpriteImage.Paired(black, white)!.Rgba[3].ShouldBe((byte)0);
    }

    [Fact]
    public void RefusesAPairThatIsNotTheSamePicture() =>
        SpriteImage.Paired(Solid(2, 2, 0, 0, 0), Solid(3, 3, 0, 0, 0)).ShouldBeNull();

    [Fact]
    public void RefusesAPictureWhoseBytesDoNotMatchItsSize() =>
        SpriteImage.Paired(new SpriteImage(4, 4, new byte[8]), new SpriteImage(4, 4, new byte[8]))
            .ShouldBeNull();

    // ---- the colour key ---------------------------------------------------------------

    [Fact]
    public void MakesPureBlackTransparentWhereItIsBeingUsedAsAKey()
    {
        var keyed = Checker(0, 0, 0, 10, 20, 30).KeyedOnBlack();

        keyed.Rgba[3].ShouldBe((byte)0);
        keyed.Rgba[7].ShouldBe((byte)255);
    }

    // The item frame's corners are (15, 9, 2) and the bar is greyscale. Keying either puts
    // holes in it.
    [Fact]
    public void LeavesAPictureThatMerelyHasBlackInItAlone()
    {
        var frame = Checker(15, 9, 2, 0, 0, 0);
        var keyed = frame.KeyedOnBlack();

        keyed.Rgba.ShouldBe(frame.Rgba);
        keyed.Rgba[7].ShouldBe((byte)255);
    }

    [Fact]
    public void DoesNotTouchTheOriginal()
    {
        var original = Checker(0, 0, 0, 10, 20, 30);

        original.KeyedOnBlack();

        original.Rgba[3].ShouldBe((byte)255);
    }

    [Fact]
    public void LeavesAPictureTooSmallToHaveCornersAlone() =>
        new SpriteImage(1, 1, [0, 0, 0, 255]).KeyedOnBlack().Rgba[3].ShouldBe((byte)255);

    [Fact]
    public void KnowsWhetherItsBytesMatchItsSize()
    {
        new SpriteImage(2, 2, new byte[16]).IsWholeImage.ShouldBeTrue();
        new SpriteImage(2, 2, new byte[15]).IsWholeImage.ShouldBeFalse();
    }

    private static SpriteImage Solid(int width, int height, byte red, byte green, byte blue)
    {
        var rgba = new byte[width * height * 4];

        for (var at = 0; at < rgba.Length; at += 4)
        {
            rgba[at] = red;
            rgba[at + 1] = green;
            rgba[at + 2] = blue;
            rgba[at + 3] = 255;
        }

        return new SpriteImage(width, height, rgba);
    }

    /// <summary>
    /// Three by three, with the four corners one colour and everything else another.
    /// </summary>
    /// <remarks>Three rather than two, because every pixel of a two by two is a corner.</remarks>
    private static SpriteImage Checker(byte cr, byte cg, byte cb, byte mr, byte mg, byte mb)
    {
        var rgba = new byte[3 * 3 * 4];

        for (var pixel = 0; pixel < 9; pixel++)
        {
            var at = pixel * 4;
            var corner = pixel is 0 or 2 or 6 or 8;

            rgba[at] = corner ? cr : mr;
            rgba[at + 1] = corner ? cg : mg;
            rgba[at + 2] = corner ? cb : mb;
            rgba[at + 3] = 255;
        }

        return new SpriteImage(3, 3, rgba);
    }
}
