using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the game detail modal. Three jobs, all of them platform
/// reach the view model must not have: dismissing, opening a patch-notes page
/// through the shell, and asking the cover cache for art at detail resolution.
/// </summary>
public partial class GameDetailsView : UserControl
{
    public GameDetailsView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user dismisses — the shell owns what "closed" means.</summary>
    public event EventHandler? CloseRequested;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RequestCover();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestCover();
    }

    private void RequestCover()
    {
        if (DataContext is not GameDetailsViewModel details)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4) — the cache snaps
        // this to a bucket, so this is one decode shared with nothing else.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        details.RequestCover(GameDetailsViewModel.CoverWidth * scaling);
    }

    /// <summary>
    /// Only a press that lands on the scrim itself closes. Without the source
    /// check, a press that started inside the card and drifted (selecting the
    /// install path, say) would dismiss the panel out from under the user.
    /// </summary>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnClosePressed(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// §5.2: "clicking the badge opens the patch notes for the updates you
    /// missed". The launcher is the platform's own handler — the app never
    /// shells out to a browser by name.
    /// </summary>
    private async void OnUpdateLinkPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: UpdateEventViewModel { Url: { } url } })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            await launcher.LaunchUriAsync(new Uri(url));
        }
    }
}
