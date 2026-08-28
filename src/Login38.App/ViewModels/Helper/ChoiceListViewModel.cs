using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Login38.App.ViewModels.Helper;

/// <summary>
/// A list the player builds by hand: add, remove, and move things around in it.
/// </summary>
/// <remarks>
/// <para>
/// One of these for each of the six lists in the helper window — what to keep up, what to
/// destroy, what to dissolve, what to shout, what never to attack, what only to attack.
/// They differ in what goes in them and in nothing else, so the reference's nine
/// near-identical handlers are one thing here.
/// </para>
/// <para>
/// <see cref="Candidate"/> is whatever the control beside the list currently holds: the
/// selected row of the list of available entries, the text typed into a box, the item
/// picked out of the bag. The list does not care which.
/// </para>
/// </remarks>
public sealed partial class ChoiceListViewModel : ObservableObject
{
    public ChoiceListViewModel() =>
        // What can be done to the list depends on what is in it, which changes without any
        // property changing. Without this the buttons grey out one action behind.
        Items.CollectionChanged += (_, _) => Refresh();

    /// <summary>What is in the list, in the order the player put it.</summary>
    public ObservableCollection<string> Items { get; } = [];

    /// <summary>Which row is selected, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    private string? _selected;

    /// <summary>What would be added.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string? _candidate;

    /// <summary>Replaces everything in the list.</summary>
    public void Load(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items.Clear();

        foreach (var item in items)
        {
            var trimmed = item?.Trim();

            if (trimmed is { Length: > 0 } && !Items.Contains(trimmed, StringComparer.Ordinal))
            {
                Items.Add(trimmed);
            }
        }

        Selected = null;
    }

    /// <summary>
    /// Whether there is anything worth adding.
    /// </summary>
    /// <remarks>
    /// A repeat is refused. Every one of these lists is a set of instructions applied in
    /// turn — destroy this, keep this up, say this — and naming the same thing twice
    /// either does nothing or does it twice by accident. The reference allowed it, and a
    /// list with two identical rows cannot be told apart afterwards.
    /// </remarks>
    private bool CanAdd => Candidate?.Trim() is { Length: > 0 } text
                           && !Items.Contains(text, StringComparer.Ordinal);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var text = Candidate?.Trim();

        if (text is not { Length: > 0 } || Items.Contains(text, StringComparer.Ordinal))
        {
            return;
        }

        Items.Add(text);
        Selected = text;
    }

    private bool CanRemove => Selected is not null && Items.Contains(Selected, StringComparer.Ordinal);

    /// <summary>
    /// Takes the selected row out, and selects what took its place.
    /// </summary>
    /// <remarks>
    /// Rather than clearing the selection, which is what the reference did — so clearing
    /// out five items meant five trips back into the list to pick the next one.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (At(Selected) is not { } index)
        {
            return;
        }

        Items.RemoveAt(index);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    private bool CanMoveUp => At(Selected) is > 0;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    private bool CanMoveDown => At(Selected) is { } index && index < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    /// <summary>Moves the selected row, and keeps it selected.</summary>
    private void Move(int by)
    {
        if (At(Selected) is not { } from)
        {
            return;
        }

        var to = from + by;

        if (to < 0 || to >= Items.Count)
        {
            return;
        }

        var moved = Items[from];

        Items.Move(from, to);
        Selected = moved;
    }

    /// <summary>Re-asks every button whether it still has anything to do.</summary>
    private void Refresh()
    {
        AddCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Where a row is, or null if it is not in the list.</summary>
    private int? At(string? item)
    {
        if (item is null)
        {
            return null;
        }

        var index = Items.IndexOf(item);

        return index < 0 ? null : index;
    }
}
