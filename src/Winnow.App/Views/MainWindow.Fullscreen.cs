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
            shared.EnrichmentSettings.ClearSecrets();
        DesktopHost.IsVisible = !fullscreen;
        TvHost.IsVisible = fullscreen;
        UpdateSetupPresentation();
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
            _tvView?.CancelStartupPresentation();
            TvHost.Content = null;
            RefreshDesktopAfterFullscreen();
        }
    }
    private async void EnsureTelevision()
    {
        if (_tvView is not null)
        {
            try
            {
                if (_tvContext is not null)
                {
                    // Arm the opaque presentation before reattaching a cached view.
                    var preparation = _tvView.PrepareAsync(_tvContext.HasLoadedPreferences
                        ? _tvContext.RefreshAsync : _tvContext.LoadAsync);
                    TvHost.Content = _tvView;
                    await preparation;
                }
            }
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
        _tvView = new FullscreenView(_tvContext, preparing: true);
        _tvView.ExitRequested += ToggleFullscreen;
        _tvView.QuitRequested += ExitFromTray;
        TvHost.Content = _tvView;
        await _tvView.PrepareAsync(_tvContext.LoadAsync);
    }
    private async void RefreshDesktopAfterFullscreen()
    {
        // An initial load or its recovery screen already owns readiness. Do not
        // replace it with a competing refresh when returning before startup finishes.
        if (_shell is null || DesktopStartupVisible) return;
        await PrepareDesktopAsync(async () =>
        {
            await _shell.Library.LoadCommand.ExecuteAsync(null);
            if (_shell.Feed.LoadCommand.ExecutionTask is { } feed) await feed;
        });
    }
}
