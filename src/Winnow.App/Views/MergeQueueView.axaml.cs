using Avalonia.Controls;
using Avalonia.Input;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the merge confirm queue: card selection and the cover
/// request.
///
/// <para>Covers are asked for once the control knows its render scaling, so the
/// 200x300 capsules decode at display resolution rather than at the source's
/// 600x900 (§5.4). The view model re-applies the request whenever the queue
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
        _queue = DataContext as MergeQueueViewModel;
        RequestCovers();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _queue = DataContext as MergeQueueViewModel;
        RequestCovers();
    }

    /// <summary>
    /// Selection follows focus into the card list. Without this, Tab moves the
    /// focus ring while the SELECTED pair stays put, so pressing S merges a
    /// different pair than the ring sits on. When answering only recorded a
    /// status this was survivable; now that answering writes to the library it
    /// is not. Selecting on focus keeps one mark on screen instead of two
    /// competing ones (§8).
    /// </summary>
    private void OnCardFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (e.Source is Control { DataContext: MergeGroupViewModel group })
        {
            _queue?.Select(group);
        }
    }

    /// <summary>Brings the card at <paramref name="index"/> into view after a keyboard move (§8).</summary>
    public void ScrollIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        CardList.ContainerFromIndex(index)?.BringIntoView();
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MergeGroupViewModel group })
        {
            _queue?.Select(group);
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
