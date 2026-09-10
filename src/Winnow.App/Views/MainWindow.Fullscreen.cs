using Avalonia.Controls;
using Avalonia.Interactivity;
using Winnow.App.Views.Fullscreen;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class MainWindow
{
    private WindowState _beforeFullscreen = WindowState.Normal;
    private bool _fullscreenReady;
    private FullscreenView? _tvView;
    private FullscreenContext? _tvContext;
    private bool _presentingTv;
    private bool _startupPresentationApplied;
    internal bool IsFullscreen => WindowState == WindowState.FullScreen;
    private void InitializeFullscreen()
    {
        _fullscreenReady = true;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (IsFullscreen && e.Key != Avalonia.Input.Key.F11 && _tvView?.HandleKey(e) == true) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
    private void DisposeFullscreen() { _fullscreenReady = false; _tvView?.Dispose(); _tvContext?.Dispose(); }
    private void ApplyStartupPresentation()
    {
        if (_startupPresentationApplied) return;
        _startupPresentationApplied = true;
        // Background launches retain the tray-first contract, even when normal launches use fullscreen.
        if (!StartHidden && _shell?.ApplicationSettings.StartInFullscreen == true && !IsFullscreen)
            ToggleFullscreen();
    }
    internal void ToggleFullscreen()
    {
        if (IsFullscreen) WindowState = _beforeFullscreen;
        else
        {
            _beforeFullscreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _gamepadKeyboard?.Close();
            WindowState = WindowState.FullScreen;
        }
        UpdateFullscreenPresentation();
    }
    private void OnFullscreenPressed(object? sender, RoutedEventArgs e) => ToggleFullscreen();
    internal void UpdateGamepadStatus(string? status) => _tvView?.UpdateController(status);
    private void UpdateFullscreenPresentation()
    {
        if (!_fullscreenReady) return;
        var fullscreen = IsFullscreen;
        if (fullscreen != _presentingTv && DataContext is MainWindowViewModel shared)
            shared.ApplicationSettings.Igdb.ClientSecret = "";
        DesktopHost.IsVisible = !fullscreen;
        TvHost.IsVisible = fullscreen;
        if (fullscreen && !_presentingTv)
        {
            _presentingTv = true;
            EnsureTelevision();
            TvHost.Content = _tvView;
            _tvView?.FocusPage();
        }
        else if (!fullscreen && _presentingTv)
        {
            RestoreMouseCursor();
            _presentingTv = false;
            TvHost.Content = null;
            RefreshDesktopAfterFullscreen();
        }
    }
    private async void EnsureTelevision()
    {
        if (_tvView is not null)
        {
            try { if (_tvContext is not null) await _tvContext.RefreshAsync(); }
            catch (Exception) { _tvContext?.Notify("Could not refresh your library. Try again."); }
            return;
        }
        if (DataContext is not MainWindowViewModel shared) return;
        if (Program.AppHost?.Services is { } services)
            _tvContext = FullscreenContext.Create(services, shared);
        else
        {
            // Preview and headless hosts have no production service container.
            var library = new LibraryViewModel(new Design.PreviewLibraryQueryRepository(),
                new Design.PreviewOwnershipRepository(), new Design.PreviewReleaseRepository(),
                new Design.PreviewWorkRepository(), new Design.PreviewUpdateEventRepository());
            _tvContext = new FullscreenContext(library, new FeedViewModel(new Design.PreviewFeedService(), library), shared);
        }
        _tvView = new FullscreenView(_tvContext);
        _tvView.ExitRequested += ToggleFullscreen;
        _tvView.QuitRequested += ExitFromTray;
        TvHost.Content = _tvView;
        try { await _tvContext.LoadAsync(); }
        catch (Exception) { _tvContext.Notify("Could not load your library. Return to fullscreen to try again."); }
    }
    private async void RefreshDesktopAfterFullscreen()
    {
        try { if (_shell is not null) await _shell.Library.LoadCommand.ExecuteAsync(null); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Library refresh after fullscreen failed: {ex}"); }
    }
}
