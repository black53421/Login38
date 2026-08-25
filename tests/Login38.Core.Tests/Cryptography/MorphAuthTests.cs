using System.Text;
using Login38.Core.Cryptography;
using Shouldly;

namespace Login38.Core.Tests.Cryptography;

public sealed class MorphAuthTests
{
    [Fact]
    public void TagIsDeterministic() =>
        MorphAuth.ComputeTag("hello world"u8).ShouldBe(MorphAuth.ComputeTag("hello world"u8));

    [Fact]
    public void TagChangesWhenAByteChanges() =>
        MorphAuth.ComputeTag("hello world"u8).ShouldNotBe(MorphAuth.ComputeTag("hello worle"u8));

    // The length prefix exists precisely to make this true; without it, CBC-MAC is
    // forgeable by appending to a known message.
    [Fact]
    public void TagChangesWhenTheLengthChanges() =>
        MorphAuth.ComputeTag("hello world"u8).ShouldNotBe(MorphAuth.ComputeTag("hello world "u8));

    [Fact]
    public void TagIsDefinedForEmptyInput() =>
        MorphAuth.ComputeTag([]).Length.ShouldBe(MorphAuth.TagLength);

    // Input that lands exactly on a block boundary takes the extra padding-only block.
    [Fact]
    public void TagDistinguishesBlockBoundaryInput() =>
        MorphAuth.ComputeTag("0123456789abcdef"u8)
            .ShouldNotBe(MorphAuth.ComputeTag("0123456789abcdefX"u8));

    [Fact]
    public void VerifyAcceptsItsOwnTag()
    {
        var data = Encoding.UTF8.GetBytes("morph pak payload");

        MorphAuth.VerifyTag(data, MorphAuth.ComputeTag(data)).ShouldBeTrue();
    }

    [Fact]
    public void VerifyRejectsATamperedTag()
    {
        var data = Encoding.UTF8.GetBytes("morph pak payload");
        var tag = MorphAuth.ComputeTag(data);
        tag[0] ^= 0xFF;

        MorphAuth.VerifyTag(data, tag).ShouldBeFalse();
    }

    [Fact]
    public void VerifyRejectsATruncatedTag()
    {
        var data = Encoding.UTF8.GetBytes("morph pak payload");
        var tag = MorphAuth.ComputeTag(data);

        MorphAuth.VerifyTag(data, tag.AsSpan(0, MorphAuth.TagLength - 1)).ShouldBeFalse();
    }
}
