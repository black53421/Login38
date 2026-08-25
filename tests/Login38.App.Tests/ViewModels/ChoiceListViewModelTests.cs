using Login38.App.ViewModels.Helper;
using Shouldly;

namespace Login38.App.Tests.ViewModels;

/// <summary>
/// Covers the list the player builds by hand.
/// </summary>
/// <remarks>
/// One of these stands behind all four lists in the helper window, so what it does with a
/// blank entry, a repeat, or the last row is what all four do.
/// </remarks>
public sealed class ChoiceListViewModelTests
{
    private static ChoiceListViewModel WithThree()
    {
        var list = new ChoiceListViewModel();
        list.Load(["一", "二", "三"]);

        return list;
    }

    [Fact]
    public void AddsWhatIsWaitingToBeAdded()
    {
        var list = new ChoiceListViewModel { Candidate = "藥水" };

        list.AddCommand.Execute(null);

        list.Items.ShouldBe(["藥水"]);
        list.Selected.ShouldBe("藥水");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddsNothingForNothing(string? candidate)
    {
        var list = new ChoiceListViewModel { Candidate = candidate };

        list.AddCommand.CanExecute(null).ShouldBeFalse();

        list.AddCommand.Execute(null);
        list.Items.ShouldBeEmpty();
    }

    [Fact]
    public void TrimsWhatItAdds()
    {
        var list = new ChoiceListViewModel { Candidate = "  藥水  " };

        list.AddCommand.Execute(null);

        list.Items.ShouldBe(["藥水"]);
    }

    // Two identical rows cannot be told apart afterwards, and neither of them does
    // anything the other does not. The reference allowed it.
    [Fact]
    public void RefusesToAddTheSameThingTwice()
    {
        var list = new ChoiceListViewModel { Candidate = "藥水" };
        list.AddCommand.Execute(null);

        list.AddCommand.CanExecute(null).ShouldBeFalse();

        list.AddCommand.Execute(null);
        list.Items.ShouldBe(["藥水"]);
    }

    [Fact]
    public void OffersToAddAgainOnceTheRepeatIsGone()
    {
        var list = new ChoiceListViewModel { Candidate = "藥水" };
        list.AddCommand.Execute(null);
        list.RemoveCommand.Execute(null);

        list.AddCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void LoadsWithoutTheBlanksAndTheRepeats()
    {
        var list = new ChoiceListViewModel();

        list.Load(["一", "  ", "一", "二", string.Empty]);

        list.Items.ShouldBe(["一", "二"]);
    }

    // Removing five things should be five clicks, not five clicks and five trips back
    // into the list to pick the next one. The reference cleared the selection.
    [Fact]
    public void SelectsWhatTookThePlaceOfWhatWasRemoved()
    {
        var list = WithThree();
        list.Selected = "二";

        list.RemoveCommand.Execute(null);

        list.Items.ShouldBe(["一", "三"]);
        list.Selected.ShouldBe("三");
    }

    [Fact]
    public void SelectsTheNewLastRowWhenTheLastOneWasRemoved()
    {
        var list = WithThree();
        list.Selected = "三";

        list.RemoveCommand.Execute(null);

        list.Selected.ShouldBe("二");
    }

    [Fact]
    public void SelectsNothingOnceTheListIsEmpty()
    {
        var list = new ChoiceListViewModel();
        list.Load(["一"]);
        list.Selected = "一";

        list.RemoveCommand.Execute(null);

        list.Items.ShouldBeEmpty();
        list.Selected.ShouldBeNull();
    }

    [Fact]
    public void RemovesNothingWithNothingSelected()
    {
        var list = WithThree();

        list.RemoveCommand.CanExecute(null).ShouldBeFalse();

        list.RemoveCommand.Execute(null);
        list.Items.Count.ShouldBe(3);
    }

    [Fact]
    public void MovesARowUpAndKeepsItSelected()
    {
        var list = WithThree();
        list.Selected = "三";

        list.MoveUpCommand.Execute(null);

        list.Items.ShouldBe(["一", "三", "二"]);
        list.Selected.ShouldBe("三");
    }

    [Fact]
    public void MovesARowDownAndKeepsItSelected()
    {
        var list = WithThree();
        list.Selected = "一";

        list.MoveDownCommand.Execute(null);

        list.Items.ShouldBe(["二", "一", "三"]);
        list.Selected.ShouldBe("一");
    }

    [Fact]
    public void WillNotMoveTheFirstRowUp()
    {
        var list = WithThree();
        list.Selected = "一";

        list.MoveUpCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void WillNotMoveTheLastRowDown()
    {
        var list = WithThree();
        list.Selected = "三";

        list.MoveDownCommand.CanExecute(null).ShouldBeFalse();
    }

    // The buttons depend on what is in the list as much as on what is selected, and the
    // list changes without any property changing.
    [Fact]
    public void OffersToMoveDownOnceThereIsSomethingToMovePast()
    {
        var list = new ChoiceListViewModel();
        list.Load(["一"]);
        list.Selected = "一";

        list.MoveDownCommand.CanExecute(null).ShouldBeFalse();

        list.Candidate = "二";
        list.AddCommand.Execute(null);
        list.Selected = "一";

        list.MoveDownCommand.CanExecute(null).ShouldBeTrue();
    }
}
