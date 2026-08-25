using Login38.Core.Text;
using Shouldly;

namespace Login38.Core.Tests.Text;

public sealed class LegacyTextCodecTests
{
    private static readonly LegacyTextCodec Auto = LegacyTextCodec.Auto;

    [Fact]
    public void DecodesTraditionalTextAsBig5()
    {
        var bytes = Auto.Encode("銀劍 (揮舞)", LegacyEncoding.Big5);

        var result = Auto.Decode(bytes);

        result.Encoding.ShouldBe(LegacyEncoding.Big5);
        result.Text.ShouldBe("銀劍 (揮舞)");
    }

    [Fact]
    public void DecodesSimplifiedTextAsGbk()
    {
        var bytes = Auto.Encode("银剑 (挥舞)", LegacyEncoding.Gbk);

        var result = Auto.Decode(bytes);

        result.Encoding.ShouldBe(LegacyEncoding.Gbk);
        result.Text.ShouldBe("银剑 (挥舞)");
    }

    // These decode cleanly as both code pages. Only the vocabulary heuristic
    // separates them, so they are the cases most likely to regress.
    [Theory]
    [InlineData("金币")]
    [InlineData("+4 死亡骑士斗篷")]
    [InlineData("面包")]
    [InlineData("蜡烛")]
    public void DecodesAmbiguousInventoryNamesAsSimplified(string name)
    {
        var bytes = Auto.Encode(name, LegacyEncoding.Gbk);

        var result = Auto.Decode(bytes);

        result.Encoding.ShouldBe(LegacyEncoding.Gbk);
        result.Text.ShouldBe(name);
    }

    [Fact]
    public void ForcedGbkModeSkipsGuessing()
    {
        var bytes = Auto.Encode("蜡烛", LegacyEncoding.Gbk);

        var result = Auto.Decode(bytes, TextEncodingMode.Gbk);

        result.Encoding.ShouldBe(LegacyEncoding.Gbk);
        result.Text.ShouldBe("蜡烛");
    }

    [Fact]
    public void ForcedBig5ModeSkipsGuessing()
    {
        var bytes = Auto.Encode("蠟燭", LegacyEncoding.Big5);

        var result = Auto.Decode(bytes, TextEncodingMode.Big5);

        result.Encoding.ShouldBe(LegacyEncoding.Big5);
        result.Text.ShouldBe("蠟燭");
    }

    [Fact]
    public void PrefersUtf8WhenTheBytesAreValidUtf8()
    {
        var result = Auto.Decode(System.Text.Encoding.UTF8.GetBytes("簡體/简体"));

        result.Encoding.ShouldBe(LegacyEncoding.Utf8);
        result.Text.ShouldBe("簡體/简体");
    }

    [Fact]
    public void StripsUtf8ByteOrderMark()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("list")];

        Auto.Decode(bytes).Text.ShouldBe("list");
    }

    [Fact]
    public void StopsAtTheFirstNulWhenReadingGameMemory()
    {
        var bytes = Auto.Encode("服务器\0tail", LegacyEncoding.Gbk);

        Auto.DecodeNullTerminated(bytes).ShouldBe("服务器");
    }

    [Fact]
    public void DecodeNullTerminatedHandlesAnUnterminatedBuffer()
    {
        var bytes = Auto.Encode("服务器", LegacyEncoding.Gbk);

        Auto.DecodeNullTerminated(bytes).ShouldBe("服务器");
    }

    [Fact]
    public void RoundTripsThroughTheSelectedCodePage()
    {
        var encoded = Auto.Encode("服务器", LegacyEncoding.Gbk);

        Auto.Decode(encoded, TextEncodingMode.Gbk).Text.ShouldBe("服务器");
    }

    [Theory]
    [InlineData("big5", TextEncodingMode.Big5)]
    [InlineData("CP950", TextEncodingMode.Big5)]
    [InlineData(" tw ", TextEncodingMode.Big5)]
    [InlineData("gbk", TextEncodingMode.Gbk)]
    [InlineData("gb2312", TextEncodingMode.Gbk)]
    [InlineData("simplified", TextEncodingMode.Gbk)]
    [InlineData("auto", TextEncodingMode.Auto)]
    [InlineData("nonsense", TextEncodingMode.Auto)]
    [InlineData(null, TextEncodingMode.Auto)]
    public void ParsesConfigValues(string? value, TextEncodingMode expected) =>
        value.ToTextEncodingMode().ShouldBe(expected);

    [Theory]
    [InlineData(TextEncodingMode.Auto, "auto")]
    [InlineData(TextEncodingMode.Big5, "big5")]
    [InlineData(TextEncodingMode.Gbk, "gbk")]
    public void RoundTripsConfigValues(TextEncodingMode mode, string expected)
    {
        mode.ToConfigValue().ShouldBe(expected);
        expected.ToTextEncodingMode().ShouldBe(mode);
    }
}
