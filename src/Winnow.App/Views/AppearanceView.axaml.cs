using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Appearance screen. All state lives on
/// <see cref="ViewModels.AppearanceViewModel"/> and every other interaction is a
/// command. View handlers own platform folder launching and keyboard scrolling.
/// </summary>
public partial class AppearanceView : UserControl
{
    public bool IsEmbedded
    {
        get => !AppearanceHeader.IsVisible;
        set => AppearanceHeader.IsVisible = !value;
    }

    public AppearanceView()
    {
        InitializeComponent();
        ScreenScroll.AddHandler(GotFocusEvent, OnScreenFocusChanged);

        // The previewer gets the populated screen; runtime leaves the
        // DataContext to the shell. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.Appearance;
        }
    }

    private void OnScreenFocusChanged(object? sender, GotFocusEventArgs e)
    {
        // Window activation restores the old focused control after inactive
        // wheel scrolling. Only deliberate keyboard navigation should reveal it.
        if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional
            && e.Source is Control control)
        {
            control.BringIntoView();
        }
    }

    /// <summary>
    /// Opens the user-theme directory in the OS file manager via the platform
    /// launcher, creating it first so the button works on a fresh install.
    /// </summary>
    private async void OnOpenThemeFolderPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppearanceViewModel appearance)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        if (appearance.PrepareThemeFolder() is not { } directory)
        {
            return;
        }

        try
        {
            await launcher.LaunchDirectoryInfoAsync(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The path is printed on screen beside the button, so a
            // refused launch is not worth a message.
        }
    }

#if DEBUG
    /// <summary>
    /// Scrolls the screen to an absolute offset. Debug only, and it exists for
    /// the same reason <c>FilterPanelView.ScrollTo</c> does: YOUR THEMES sits
    /// under a row of five theme cards and is below the fold on an 820px
    /// window, so a screenshot cannot show the folder, the contrast report or
    /// the validation output without a scroll — and injected input is not
    /// trustworthy here (SetForegroundWindow fails silently on this machine).
    /// Same convention as --open-appearance: a flag that lands the window on the
    /// state to be reviewed rather than a synthetic click that may or may not
    /// have landed.
    /// </summary>
    public void ScrollTo(double y)
        => ScreenScroll.Offset = ScreenScroll.Offset.WithY(y);
#endif
}
