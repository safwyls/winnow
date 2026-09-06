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
/// them passes through a control in this view.
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (change.GetNewValue<bool>())
        {
            // Keep focus inside the overlay without an initial keyboard ring.
            // Tabbing still reveals the ring through :focus-visible. Wait until
            // the controls have been laid out before moving focus.
            Dispatcher.UIThread.Post(
                () => CloseButton.Focus(NavigationMethod.Pointer),
                DispatcherPriority.Input);

            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

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
