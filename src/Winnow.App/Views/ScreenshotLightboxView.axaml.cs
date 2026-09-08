using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the screenshot lightbox (design-system.md §10.7). It owns
/// two things the view model cannot: where focus goes when the overlay appears,
/// and telling the window when it has gone so the modal can return focus to the
/// originating thumbnail. Both hang off <c>IsVisible</c> rather than off the
/// close button, because there are four ways out — the close control, a press
/// on the scrim, Escape, and the modal closing underneath — and only one of
/// them passes through a control in this view. The first appearance of a
/// session is the one exception, and it is the attach below: the overlay is
/// built the first time it is opened, so it arrives already visible.
/// </summary>
public partial class ScreenshotLightboxView : UserControl
{
    public ScreenshotLightboxView()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the overlay has gone. <c>MainWindow</c> returns
    /// focus to the originating thumbnail on this event.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// The first appearance of a session, which <see cref="OnPropertyChanged"/>
    /// cannot see: the overlay is built the first time it is opened
    /// (<see cref="LazyPane"/>), so it arrives already visible and its
    /// <c>IsVisible</c> never turns on. Every later open is a real change and is
    /// taken below.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (IsVisible)
        {
            TakeFocus();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (change.GetNewValue<bool>())
        {
            TakeFocus();
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Keeps focus inside the overlay without an initial keyboard ring. Tabbing
    /// still reveals the ring through <c>:focus-visible</c>. Posted, so the
    /// controls have been laid out before focus moves.
    /// </summary>
    private void TakeFocus()
        => Dispatcher.UIThread.Post(
            () => CloseButton.Focus(NavigationMethod.Pointer),
            DispatcherPriority.Input);

    /// <summary>
    /// Only a press that lands on the scrim itself closes. Without the source
    /// check a press that started on the image and drifted would dismiss the
    /// overlay out from under the user — the same guard the detail modal's own
    /// scrim carries.
    /// </summary>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender))
        {
            return;
        }

        (DataContext as ScreenshotLightboxViewModel)?.CloseCommand.Execute(null);
        e.Handled = true;
    }
}
