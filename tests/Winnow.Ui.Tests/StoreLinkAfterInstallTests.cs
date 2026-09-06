using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class StoreLinkAfterInstallTests
{
    [AvaloniaFact]
    public void Open_details_refreshes_install_to_play_without_losing_the_store_link()
    {
        var now = DateTime.UtcNow;
        var storefront = new StorefrontDetails("https://store.epicgames.com/en-US/p/moonlighter", null);
        GameTileViewModel Tile(bool installed) => TileFixture.Tile(now,
            [TileEntry.For(1, 1, 1, "epic", 0, null,
                ownership: new Ownership { ReleaseId = 1, Store = "epic", Installed = installed },
                epicLaunchKey: EpicLaunchKey.Create("sample-namespace", "sample-catalog", "Moonlighter"),
                storefront: storefront)], 1, LibraryBuckets.NeverPlayed, title: "Moonlighter");
        var initial = Tile(false);
        initial.PrimaryActionCommand = new RelayCommand<GameTileViewModel>(_ => { });
        var model = new GameDetailsViewModel(initial, "Never played", [], now);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();
        try
        {
            var launch = view.FindControl<Button>("LaunchButton")!;
            Assert.Equal("Install", launch.Content);
            var installed = Tile(true);
            GameTileViewModel? launched = null;
            installed.PrimaryActionCommand = new RelayCommand<GameTileViewModel>(tile => launched = tile);
            typeof(GameDetailsViewModel).GetMethod("RefreshTileActions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(model, [installed]);
            Flush();

            Assert.Same(model, view.DataContext);
            Assert.Equal("Play", launch.Content);
            Assert.Same(installed, launch.CommandParameter);
            var store = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                button.DataContext is GameLink { Label: "Store page" });
            var bounds = BoundsInWindow(store, window);
            AssertReadableAndClickable(store, window, bounds);
            var click = BoundsInWindow(launch, window).Center;
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Flush();
            Assert.Same(installed, launched);
            AssertReadableAndClickable(store, window, bounds);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1200, 640)]
    [InlineData(1280, 820)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public async Task Epic_store_link_keeps_its_text_and_hit_target_during_and_after_install_dispatch(int width, int height)
    {
        var now = DateTime.UtcNow;
        var entry = TileEntry.For(1, 1, 1, "epic", 0, null,
            ownership: new Ownership { ReleaseId = 1, Store = "epic", Installed = false },
            epicLaunchKey: EpicLaunchKey.Create("sample-namespace", "sample-catalog", "Moonlighter"),
            storefront: new StorefrontDetails("https://store.epicgames.com/en-US/p/moonlighter", null));
        var tile = TileFixture.Tile(now, [entry], 1, LibraryBuckets.NeverPlayed, title: "Moonlighter");
        var dispatch = new TaskCompletionSource<LaunchDispatch>();
        var dispatchCount = 0;
        var command = new AsyncRelayCommand<GameTileViewModel>(async _ =>
        {
            dispatchCount++;
            await dispatch.Task;
        });
        tile.PrimaryActionCommand = command;
        var model = new GameDetailsViewModel(tile, "Never played", [], now);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        Flush();

        try
        {
            var launch = view.FindControl<Button>("LaunchButton")!;
            Assert.Equal("Install", launch.Content);
            var store = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                button.DataContext is GameLink { Label: "Store page" });
            var bounds = BoundsInWindow(store, window);
            var links = model.Links;
            AssertReadableAndClickable(store, window, bounds);

            launch.Focus(NavigationMethod.Tab);
            var click = BoundsInWindow(launch, window).Center;
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Flush();
            Assert.Equal(1, dispatchCount);
            Assert.True(command.IsRunning);
            Assert.False(launch.IsEffectivelyEnabled);
            Assert.Same(links, model.Links);
            AssertReadableAndClickable(store, window, bounds);

            dispatch.SetResult(LaunchDispatch.HandedOff);
            await command.ExecutionTask!;
            await Task.Yield();
            Flush();
            window.MouseMove(new Point(8, 8));
            Flush();
            Assert.False(command.IsRunning);
            Assert.Same(links, model.Links);
            AssertReadableAndClickable(store, window, bounds);
            Assert.True(store.Focus(NavigationMethod.Tab));
            Flush();
            AssertReadableAndClickable(store, window, bounds);

            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"moonlighter-after-install-{width}.png"));
            }
        }
        finally
        {
            dispatch.TrySetResult(LaunchDispatch.HandedOff);
            window.Close();
        }
    }

    private static void AssertReadableAndClickable(Button store, Window window, Rect expectedBounds)
    {
        Assert.True(store.IsEffectivelyVisible);
        Assert.True(store.IsEffectivelyEnabled);
        Assert.Equal(1, store.Opacity);
        Assert.Equal(expectedBounds, BoundsInWindow(store, window));
        var label = Assert.Single(store.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal("Store page", label.Text);
        Assert.True(label.IsEffectivelyVisible);
        Assert.True(label.Bounds.Width > 0);
        Assert.True(label.Bounds.Height > 0);
        var hit = window.InputHitTest(expectedBounds.Center) as Control;
        Assert.Same(store, hit?.FindAncestorOfType<Button>(includeSelf: true));
    }

    private static Rect BoundsInWindow(Control control, Window window)
        => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
