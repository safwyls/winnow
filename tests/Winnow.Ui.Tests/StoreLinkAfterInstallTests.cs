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
using Winnow.Core.Reading;
using Winnow.Core.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class StoreLinkAfterInstallTests
{
    [AvaloniaFact]
    public void Steam_uninstall_menu_row_disappears_when_refreshed_ownership_is_uninstalled()
    {
        var now = DateTime.UtcNow;
        GameTileViewModel Tile(bool installed) => TileFixture.Tile(now,
            [TileEntry.For(1, 1, 1, "steam", 0, null, steamAppId: "620",
                ownership: new Ownership { ReleaseId = 1, Store = "steam", Installed = installed })],
            1, LibraryBuckets.NeverPlayed, title: "Portal 2");
        var model = new GameDetailsViewModel(Tile(true), "Never played", [], now);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();
        try
        {
            var more = view.FindControl<Button>("MoreActionsButton")!;
            var click = BoundsInWindow(more, window).Center;
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Flush();
            var menu = Assert.IsType<MenuFlyout>(more.Flyout);
            Assert.True(menu.IsOpen);
            var row = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Name == "ManageInstallationItem");
            Assert.True(row.IsVisible);
            Assert.Equal("Uninstall in Steam", row.Header);
            Assert.True(row.Focus(NavigationMethod.Tab));
            menu.Hide();
            typeof(GameDetailsViewModel).GetMethod("RefreshTileActions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(model, [Tile(false)]);
            Flush();
            menu.ShowAt(more);
            Flush();
            Assert.False(row.IsVisible);
            Assert.Equal("Install", model.PrimaryAction!.Label);
            menu.Hide();
        }
        finally { window.Close(); }
    }

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
        var reader = new RecordingLinkReader();
        var model = new GameDetailsViewModel(initial, "Never played", [], now, patchNotes: reader);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();
        try
        {
            var launch = view.FindControl<Button>("LaunchButton")!;
            Assert.Equal("Install", ButtonLabel(launch));
            Assert.True(view.FindControl<Control>("HeaderInstallGlyph")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<Control>("HeaderPlayGlyph")!.IsEffectivelyVisible);
            var before = OpenStoreMenu(view, window);
            AssertReadableAndClickable(before, window, BoundsInWindow(before, window));
            Click(before, window);
            Assert.Single(reader.Opened);
            var installed = Tile(true);
            GameTileViewModel? launched = null;
            installed.PrimaryActionCommand = new RelayCommand<GameTileViewModel>(tile => launched = tile);
            typeof(GameDetailsViewModel).GetMethod("RefreshTileActions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(model, [installed]);
            Flush();

            Assert.Same(model, view.DataContext);
            Assert.Equal("Play", ButtonLabel(launch));
            Assert.False(view.FindControl<Control>("HeaderInstallGlyph")!.IsEffectivelyVisible);
            Assert.True(view.FindControl<Control>("HeaderPlayGlyph")!.IsEffectivelyVisible);
            Assert.Same(installed, launch.CommandParameter);
            var store = OpenStoreMenu(view, window);
            var bounds = BoundsInWindow(store, window);
            AssertReadableAndClickable(store, window, bounds);
            Assert.NotSame(before, store);
            Click(store, window);
            Assert.Equal(2, reader.Opened.Count);
            var click = BoundsInWindow(launch, window).Center;
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Flush();
            Assert.Same(installed, launched);
            Assert.Same(store, OpenStoreMenu(view, window));
            AssertReadableAndClickable(store, window, bounds);
            CloseMenu(view);

            // Reopening details reconstructs its outbound menu rows from the
            // current installed tile, just as a library refresh does.
            using var reopened = new GameDetailsViewModel(installed, "Never played", [], now, patchNotes: reader);
            view.DataContext = reopened;
            Flush();
            Assert.Equal("Play", ButtonLabel(launch));
            var reopenedStore = OpenStoreMenu(view, window);
            AssertReadableAndClickable(reopenedStore, window, bounds);
            Click(reopenedStore, window);
            Assert.Equal(3, reader.Opened.Count);
            Assert.All(reader.Opened, uri => Assert.Equal(storefront.StoreUrl, uri.AbsoluteUri));
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
        var reader = new RecordingLinkReader();
        var model = new GameDetailsViewModel(tile, "Never played", [], now, patchNotes: reader);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        Flush();

        try
        {
            var launch = view.FindControl<Button>("LaunchButton")!;
            Assert.Equal("Install", ButtonLabel(launch));
            var store = OpenStoreMenu(view, window);
            var bounds = BoundsInWindow(store, window);
            var more = view.FindControl<Button>("MoreActionsButton")!;
            var triggerBounds = more.Bounds;
            var links = model.Links;
            await AssertReadableAndClickableAfterLayoutAsync(store, window, bounds);
            CloseMenu(view);

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
            Assert.Equal(triggerBounds, more.Bounds);
            Assert.Same(store, OpenStoreMenu(view, window));
            await AssertReadableAndClickableAfterLayoutAsync(store, window, bounds);
            Click(store, window);
            Assert.Single(reader.Opened);

            dispatch.SetResult(LaunchDispatch.HandedOff);
            await command.ExecutionTask!;
            await Task.Yield();
            Flush();
            window.MouseMove(new Point(8, 8));
            Flush();
            Assert.False(command.IsRunning);
            Assert.Same(links, model.Links);
            Assert.Equal(triggerBounds, more.Bounds);
            Assert.Same(store, OpenStoreMenu(view, window));
            await AssertReadableAndClickableAfterLayoutAsync(store, window, bounds);
            Click(store, window);
            Assert.Equal(2, reader.Opened.Count);
            Assert.All(reader.Opened, uri => Assert.Equal("https://store.epicgames.com/en-US/p/moonlighter", uri.AbsoluteUri));
            OpenStoreMenu(view, window);
            Assert.True(store.Focus(NavigationMethod.Tab));
            await AssertReadableAndClickableAfterLayoutAsync(store, window, bounds);

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

    private static void AssertReadableAndClickable(MenuItem store, Window window, Rect expectedBounds)
    {
        Assert.True(store.IsEffectivelyVisible);
        Assert.True(store.IsEffectivelyEnabled);
        Assert.Equal(1, store.Opacity);
        var actualBounds = BoundsInWindow(store, window);
        Assert.Equal(expectedBounds.Size, actualBounds.Size);
        // Popup placement rounds screen coordinates each time More opens.
        // Its trigger stays fixed; a one-pixel origin adjustment must not
        // change the row's size or the actual clickable region.
        Assert.InRange(Math.Abs(expectedBounds.X - actualBounds.X), 0, 1);
        Assert.InRange(Math.Abs(expectedBounds.Y - actualBounds.Y), 0, 1);
        Assert.Equal("Store page", store.Header);
        var label = Assert.Single(store.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Store page");
        Assert.Equal("Store page", label.Text);
        Assert.True(label.IsEffectivelyVisible);
        Assert.True(label.Bounds.Width > 0);
        Assert.True(label.Bounds.Height > 0);
        var hit = window.InputHitTest(actualBounds.Center) as Control;
        Assert.Same(store, hit?.FindAncestorOfType<MenuItem>(includeSelf: true));
    }

    private static async Task AssertReadableAndClickableAfterLayoutAsync(
        MenuItem store, Window window, Rect expectedBounds)
    {
        // Headless rendering and the input tree settle on separate dispatcher
        // passes. At large window sizes the visual can already have final
        // bounds while InputHitTest still sees the prior tree for one pass.
        // Drain actual layout/render work instead of sleeping for an arbitrary
        // duration, then retain the exact same hit-target assertion.
        for (var pass = 0; pass < 4; pass++)
        {
            Flush();
            var hit = window.InputHitTest(BoundsInWindow(store, window).Center) as Control;
            if (ReferenceEquals(store, hit?.FindAncestorOfType<MenuItem>(includeSelf: true)))
            {
                AssertReadableAndClickable(store, window, expectedBounds);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }

        AssertReadableAndClickable(store, window, expectedBounds);
    }

    private static Rect BoundsInWindow(Control control, Window window)
        => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static string? ButtonLabel(Button button)
        => button.GetVisualDescendants().OfType<TextBlock>().Single().Text;

    private static MenuItem OpenStoreMenu(GameDetailsView view, Window window)
    {
        var more = view.FindControl<Button>("MoreActionsButton")!;
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        if (!menu.IsOpen) Click(more, window);
        Assert.True(menu.IsOpen);
        return Assert.Single(menu.Items.OfType<MenuItem>(), item => item.DataContext is GameLink { Label: "Store page" });
    }

    private static void CloseMenu(GameDetailsView view)
    {
        Assert.IsType<MenuFlyout>(view.FindControl<Button>("MoreActionsButton")!.Flyout).Hide();
        Flush();
    }

    private static void Click(Control control, Window window)
    {
        var point = BoundsInWindow(control, window).Center;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush();
    }

    // Every outbound URL reaches the optional reader before platform launch.
    // Capture there so pointer activation is verified without opening a browser.
    private sealed class RecordingLinkReader : IPatchNotesReader
    {
        public bool IsAvailable => true;
        public List<Uri> Opened { get; } = [];
        public PatchNotesOutcome Open(Uri url, string title)
        {
            Opened.Add(url);
            return PatchNotesOutcome.Opened;
        }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
