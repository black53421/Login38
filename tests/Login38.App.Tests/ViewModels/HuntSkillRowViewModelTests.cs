using Login38.App.ViewModels.Helper;
using Login38.Aux.Hunt;
using Shouldly;

namespace Login38.App.Tests.ViewModels;

/// <summary>
/// Covers one turn of the rotation as the window edits it.
/// </summary>
/// <remarks>
/// The kind is carried as an index because binding an enum to a combo box needs a converter
/// and two entries in a fixed order need nothing at all. That is a cheap trick with one
/// sharp edge — the index and the enum agreeing by position — and these hold it, because
/// getting it wrong turns every skill row into a weapon row and the hunt casts nothing at
/// all while looking entirely healthy.
/// </remarks>
public sealed class HuntSkillRowViewModelTests
{
    [Fact]
    public void StartsOnASkillRatherThanTheWeapon()
    {
        var row = new HuntSkillRowViewModel();

        row.StepIndex.ShouldBe((int)HuntStep.Skill);
        row.IsSkill.ShouldBeTrue();
        row.ToRow().Step.ShouldBe(HuntStep.Skill);
    }

    [Theory]
    [InlineData(HuntStep.Skill)]
    [InlineData(HuntStep.Weapon)]
    public void CarriesTheKindBothWays(HuntStep step)
    {
        var row = new HuntSkillRowViewModel();

        row.Load(new HuntSkill { Enabled = true, Step = step, Name = "bolt" });

        row.StepIndex.ShouldBe((int)step);
        row.ToRow().Step.ShouldBe(step);
    }

    // The name box greys out for a weapon row, and it is the row that says so rather than a
    // trigger in the window, so the rule is in one place and can be read here.
    [Fact]
    public void SaysWhenTheNameStopsMattering()
    {
        var row = new HuntSkillRowViewModel { StepIndex = (int)HuntStep.Weapon };

        row.IsSkill.ShouldBeFalse();

        row.StepIndex = (int)HuntStep.Skill;

        row.IsSkill.ShouldBeTrue();
    }

    // Kept rather than cleared. Switching a row to the weapon and back is something a player
    // does while working out an order, and it should not cost them the typing.
    [Fact]
    public void KeepsTheNameThroughAWeaponRow()
    {
        var row = new HuntSkillRowViewModel { Name = "bolt", StepIndex = (int)HuntStep.Weapon };

        row.ToRow().Name.ShouldBe("bolt");
    }

    // Anything the combo cannot produce is a skill, because a settings file is a file and
    // the alternative is a row that silently casts nothing.
    [Fact]
    public void TreatsAnythingItDoesNotRecogniseAsASkill()
    {
        var row = new HuntSkillRowViewModel { StepIndex = 7 };

        row.ToRow().Step.ShouldBe(HuntStep.Skill);
    }
}
