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

    /// <summary>Selection follows focus into the card list. Without this, Tab
    /// moves the focus ring while the selected group stays put and the shortcut
    /// answers a card the ring is not on. Answering writes to the library, so
    /// the two marks must not disagree (§8).</summary>
    private void OnCardFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (e.Source is Control { DataContext: MergeGroupViewModel group })
        {
            _queue?.Select(group);
        }
    }

    /// <summary>Selection follows focus on the expansion surface. Without it the
    /// surface had no selection input at all, so G grouped the first card whatever
    /// the user was looking at.</summary>
    private void OnExpansionCardFocus(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (e.Source is Control { DataContext: ExpansionGroupViewModel group })
        {
            _queue?.SelectExpansion(group);
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

    /// <summary>Brings the expansion card at <paramref name="index"/> into view after a keyboard move (§8).</summary>
    /// <param name="index">The index of the card to scroll to, or -1 to do nothing.</param>
    public void ScrollExpansionIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        ExpansionList.ContainerFromIndex(index)?.BringIntoView();
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MergeGroupViewModel group })
        {
            _queue?.Select(group);
        }
    }

    private void OnExpansionCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ExpansionGroupViewModel group })
        {
            _queue?.SelectExpansion(group);
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
