using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Merges screen: row hover and focus, promotion by
/// click, the sort flyout, and the cover request.
///
/// <para>Covers are asked for once the control knows its render scaling, so
/// the 34x51 thumbnails decode at display resolution rather than at the
/// source's 600x900. The view model re-applies the request whenever the queue
/// reloads, so attach order does not matter.</para>
/// </summary>
public partial class MergeQueueView : UserControl
{
    private MergeQueueViewModel? _queue;

    public MergeQueueView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_queue is not null)
        {
            _queue.FocusRequested -= OnFocusRequested;
        }

        _queue = DataContext as MergeQueueViewModel;
        if (_queue is not null)
        {
            _queue.FocusRequested += OnFocusRequested;
        }

        RequestCovers();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _queue = DataContext as MergeQueueViewModel;
        RequestCovers();
    }

    /// <summary>The pointer arrives: the row fills and its detail takes the reason slot.</summary>
    private void OnRowEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control { DataContext: MergeRowViewModel row } || _queue is null)
        {
            return;
        }

        row.IsHovered = true;
        if (_queue.CardOf(row) is { } card)
        {
            card.HoveredRow = row;
        }
    }

    /// <summary>The pointer leaves: the fill restores and the reason returns.</summary>
    private void OnRowExited(object? sender, PointerEventArgs e)
    {
        if (sender is not Control { DataContext: MergeRowViewModel row } || _queue is null)
        {
            return;
        }

        row.IsHovered = false;
        if (_queue.CardOf(row) is { HoveredRow: { } hovered } card && ReferenceEquals(hovered, row))
        {
            card.HoveredRow = null;
        }
    }

    /// <summary>
    /// A click on the row opens the game's details and takes the keyboard
    /// cursor with it. The radio and the checkbox handle their own presses,
    /// so a click on either never reaches here.
    /// </summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MergeRowViewModel row } control || _queue is null)
        {
            return;
        }

        control.Focus();
        _queue.OpenDetailsCommand.Execute(row);
    }

    /// <summary>
    /// The cursor follows focus into the rows. Without this, Tab moves the
    /// focus ring while the cursor stays put and S answers a card the ring
    /// is not on. Answering writes to the library, so the two must not
    /// disagree (§8).
    /// </summary>
    private void OnRowFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is Control { DataContext: MergeRowViewModel row })
        {
            _queue?.FocusRow(row);
        }
    }

    /// <summary>A sort row was chosen: apply it and close the menu.</summary>
    private void OnSortItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MergeSortOptionViewModel option })
        {
            _queue?.SelectSortCommand.Execute(option);
        }

        SortButton.Flyout?.Hide();
    }

    // The view model moved the cursor (a keyboard step, or the row that took
    // an answered card's place): put keyboard focus on that row and bring it
    // into view. The row's container is found by data context because the
    // rows live three ItemsControls deep.
    private void OnFocusRequested(MergeRowViewModel row)
    {
        foreach (var descendant in this.GetVisualDescendants())
        {
            if (descendant is Border { Focusable: true } border
                && ReferenceEquals(border.DataContext, row))
            {
                border.Focus();
                border.BringIntoView();
                return;
            }
        }
    }

    private void RequestCovers()
    {
        if (_queue is null)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _queue.RequestCovers(MergeQueueViewModel.CoverWidth * scaling);
    }
}
