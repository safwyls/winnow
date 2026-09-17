using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Avalonia.Interactivity;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class RailListControlsTests
{
    [AvaloniaFact]
    public async Task Statistics_sits_with_screens_and_stays_reachable_on_both_surfaces()
    {
        var shell = PreviewData.Shell;
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.Library.Prompt?.CancelCommand.Execute(null);
        shell.ShowLibraryCommand.Execute(null);
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            Assert.True(await window.StartupLibraryReady);
            Flush();
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var stats = buttons.Single(button => ReferenceEquals(button.Command, shell.ToggleAccountStatsCommand));
            var feed = buttons.Single(button => ReferenceEquals(button.Command, shell.ShowFeedCommand));
            var merges = buttons.Single(button => ReferenceEquals(button.Command, shell.ToggleMergeQueueCommand));
            var all = buttons.Single(button => ReferenceEquals(button.CommandParameter, shell.Library.AllGames));
            Assert.True(feed.Bounds.Top < merges.Bounds.Top);
            Assert.True(merges.Bounds.Top < stats.Bounds.Top);
            Assert.True(stats.Bounds.Top < all.Bounds.Top);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "ACCOUNT");
            Activate(window, stats);
            if (shell.ToggleAccountStatsCommand.ExecutionTask is { } load) await load;
            Flush();
            Assert.True(shell.IsAccountStatsVisible);
            Assert.Equal("STATS", AutomationProperties.GetName(stats));
            Assert.Contains(stats.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "STATS");
            Activate(window, all); Flush();
            Assert.False(shell.IsAccountStatsVisible);
        }
        finally { window.Close(); shell.ShowLibraryCommand.Execute(null); }

        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenActivityPage(context);
        FullscreenPage? opened = null;
        context.PageRequested += target => opened = target;
        var tv = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            tv.Show(); await page.PendingRefresh; Flush();
            var summary = page.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Library summary");
            Assert.True(summary.Focus(NavigationMethod.Tab));
            tv.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            tv.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.IsType<FullscreenLibrarySummaryPage>(opened);
        }
        finally { tv.Close(); opened?.Dispose(); }
    }

    [AvaloniaFact]
    public void Context_menu_offers_removal_only_for_a_static_list_selection()
    {
        var shell = PreviewData.Shell;
        var library = shell.Library;
        var originalOpen = library.Lists.Open;
        var originalSelection = library.SelectedTiles;
        var window = new MainWindow();
        try
        {
            window.Show();
            window.DataContext = shell;
            Flush();
            var menu = window.GetVisualDescendants().OfType<Panel>()
                .Select(panel => panel.ContextMenu).Single(context => context is not null)!;
            // The popup inherits its owner only when opened. Supplying that context
            // here lets us exercise membership visibility without opening a native popup.
            menu.DataContext = shell;
            var remove = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove from list"));
            library.Lists.Open = null;
            library.SelectedTiles = [PreviewData.Tile];
            Flush();
            Assert.False(remove.IsVisible);
            library.Lists.Open = new GameListViewModel(GameList.Live("Live", LibraryFilter.Empty));
            library.SelectedTiles = [PreviewData.Tile];
            Flush();
            Assert.False(remove.IsVisible);
            library.Lists.Open = new GameListViewModel(GameList.Manual("Static"));
            library.SelectedTiles = [PreviewData.Tile];
            Flush();
            Assert.True(remove.IsVisible);
            Assert.Same(library.RemoveFromOpenListCommand, remove.Command);
            library.SelectedTiles = [];
            Flush();
            Assert.False(remove.IsVisible);
        }
        finally
        {
            library.Lists.Open = originalOpen;
            library.SelectedTiles = originalSelection;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Sections_collapse_independently_and_footer_creates_either_kind()
    {
        var shell = PreviewData.Shell;
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.Library.Prompt?.CancelCommand.Execute(null);
        shell.ShowLibraryCommand.Execute(null);
        var lists = shell.Library.Lists;
        var manual = new GameListViewModel(GameList.Manual("Rail test static") with { Id = -101 });
        var live = new GameListViewModel(GameList.Live("Rail test live", LibraryFilter.Empty) with { Id = -102 });
        lists.Lists.Add(manual);
        lists.LiveLists.Add(live);
        lists.AreListsExpanded = lists.AreLiveListsExpanded = true;
        lists.IsCreateMenuOpen = false;
        var window = new MainWindow();
        try
        {
            window.Show();
            window.DataContext = shell;
            Flush();
            var controls = window.GetVisualDescendants().OfType<Button>().ToArray();
            var staticHeader = controls.Single(button => AutomationProperties.GetName(button) == "LISTS");
            var liveHeader = controls.Single(button => AutomationProperties.GetName(button) == "LIVE LISTS");
            var staticRows = window.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => ReferenceEquals(control.ItemsSource, lists.Lists));
            var liveRows = window.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => ReferenceEquals(control.ItemsSource, lists.LiveLists));
            Activate(window, staticHeader);
            Assert.False(staticRows.IsVisible);
            Assert.True(liveRows.IsVisible);
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(staticHeader));
            Activate(window, liveHeader);
            Assert.False(liveRows.IsVisible);
            Activate(window, staticHeader);
            Assert.True(staticRows.IsVisible);
            Assert.False(liveRows.IsVisible);

            var create = controls.Single(button => AutomationProperties.GetName(button) == "New list");
            var settings = window.FindControl<Button>("SettingsButton")!;
            var createBounds = new Rect(create.TranslatePoint(default, window)!.Value, create.Bounds.Size);
            var settingsBounds = new Rect(settings.TranslatePoint(default, window)!.Value, settings.Bounds.Size);
            Assert.True(createBounds.Right < settingsBounds.Left);
            Assert.Equal(createBounds.Center.Y, settingsBounds.Center.Y);
            Assert.True(settingsBounds.Bottom <= window.ClientSize.Height);

            foreach (var (label, question) in new[] { ("Static list", "Name this list"), ("Live list", "Name this live list") })
            {
                Activate(window, create);
                var choice = controls.Single(button => Equals(button.Content, label));
                Assert.True(choice.IsEffectivelyVisible);
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(ToolTip.GetTip(choice))));
                Activate(window, choice);
                Assert.False(lists.IsCreateMenuOpen);
                Assert.Equal(question, shell.Library.Prompt!.Question);
                shell.Library.Prompt.CancelCommand.Execute(null);
                Flush();
                Assert.Same(create, window.FocusManager!.GetFocusedElement());
                Flush();
            }
        }
        finally
        {
            shell.Library.Prompt?.CancelCommand.Execute(null);
            lists.Lists.Remove(manual);
            lists.LiveLists.Remove(live);
            lists.AreListsExpanded = lists.AreLiveListsExpanded = true;
            lists.IsCreateMenuOpen = false;
            window.Close();
        }
    }

    private static void Activate(Window window, Button button)
    {
        // The input helper renders before dispatch. Settle layout and pending
        // focus restoration before choosing the control that receives Enter.
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Flush();
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Flush();
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();
}
