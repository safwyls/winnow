using Avalonia.Controls;
using Avalonia.Input;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

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
        if (sender is Control { DataContext: MergeCandidateViewModel candidate })
        {
            _queue?.Select(candidate);
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
