using Login38.Core.Text;
using Login38.Patching.Patches;
using Shouldly;

namespace Login38.Patching.Tests.Patches;

/// <summary>
/// Covers when the simplified Chinese switch takes effect.
/// </summary>
/// <remarks>
/// The writes themselves go to fixed addresses inside a running client, so what is
/// checkable here is the decision to make them — which is the part the reference got
/// wrong, by reaching it only through an environment variable no operator sets.
/// </remarks>
public sealed class SimplifiedChineseTextPatchTests
{
    // The setting that already selects GBK for every other string the launcher handles.
    // Anything else and an operator publishing in simplified Chinese gets a client that
    // renders their text as noise.
    [Fact]
    public void FollowsTheOperatorsEncoding() =>
        SimplifiedChineseTextPatch.WantsSimplified(null, TextEncodingMode.Gbk).ShouldBeTrue();

    [Theory]
    [InlineData(TextEncodingMode.Big5)]
    [InlineData(TextEncodingMode.Auto)]
    public void LeavesOtherEncodingsAlone(TextEncodingMode encoding) =>
        SimplifiedChineseTextPatch.WantsSimplified(null, encoding).ShouldBeFalse();

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    public void TurnsOnForAnOverride(string value) =>
        SimplifiedChineseTextPatch.WantsSimplified(value, TextEncodingMode.Big5).ShouldBeTrue();

    // The override has to work in both directions, or there is no way to check whether
    // this patch is what broke a GBK server's client short of editing its config.
    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    public void TurnsOffForAnOverride(string value) =>
        SimplifiedChineseTextPatch.WantsSimplified(value, TextEncodingMode.Gbk).ShouldBeFalse();

    // Unset is not the same as set to false: unset means "no opinion", and the encoding
    // decides.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TreatsAnUnsetOverrideAsNoOpinion(string? value)
    {
        SimplifiedChineseTextPatch.WantsSimplified(value, TextEncodingMode.Gbk).ShouldBeTrue();
        SimplifiedChineseTextPatch.WantsSimplified(value, TextEncodingMode.Big5).ShouldBeFalse();
    }

    // Set by hand on a player's machine. A value nobody can parse must not be read as a
    // request to change the client's language.
    [Fact]
    public void IgnoresAnUnparseableOverride()
    {
        SimplifiedChineseTextPatch.WantsSimplified("maybe", TextEncodingMode.Big5).ShouldBeFalse();
        SimplifiedChineseTextPatch.WantsSimplified("maybe", TextEncodingMode.Gbk).ShouldBeTrue();
    }
}
