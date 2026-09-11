using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LinkDestinationTests
{
    [AvaloniaFact]
    public async Task Library_created_details_receive_the_shared_router()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(GameLinkRouter.SettingKey, "browser");
        var dispatcher = new UriRecorder();
        var router = new GameLinkRouter(dispatcher, settings, new Clients());
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownerships = new OwnershipRepository(db.Factory);
        var work = await works.InsertAsync(new Work { Name = "Link fixture" });
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Link fixture" });
        await ownerships.UpsertAsync(new OwnershipUpsert(release, "steam", null, null, null, null));
        using var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), ownerships, releases,
            works, new UpdateEventRepository(db.Factory), linkRouter: router);
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));
        var link = GameLink.Create("Read", "https://example.com/news")!;
        Assert.True(await library.Details!.OpenReadingLinkAsync(link));
        Assert.Equal(link.Uri, Assert.Single(dispatcher.Opened).ToString());
    }

    [AvaloniaFact]
    public async Task Desktop_and_controller_share_destination_and_persist_it()
    {
        using var db = new TempDatabase();
        var repository = new SettingsRepository(db.Factory);
        var settings = new ApplicationSettingsViewModel(repository, storeClients: new Clients());
        await settings.LoadAsync();
        var shell = new MainWindowViewModel(PreviewData.Library, PreviewData.MergeQueue, PreviewData.Stores,
            PreviewData.Appearance, PreviewData.Feed, PreviewData.AccountStats, PreviewData.LibrarySettings, applicationSettings: settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var television = new FullscreenSettingsPage(context, "Application");
        var desktop = new ApplicationSettingsView { DataContext = settings };
        var window = new Window { Width = 1000, Height = 900, Content = desktop };
        var tv = new Window { Width = 1920, Height = 1080, Content = television };
        try
        {
            window.Show(); tv.Show(); Dispatcher.UIThread.RunJobs();
            var select = Assert.Single(desktop.GetVisualDescendants().OfType<ComboBox>(), c => AutomationProperties.GetName(c) == "Open links in");
            select.SelectedIndex = 1;
            await settings.PendingSave;
            var adjust = Assert.Single(television.GetVisualDescendants().OfType<Button>(), c => AutomationProperties.GetName(c) == "Open links in");
            Assert.Contains(adjust.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("System browser", StringComparison.Ordinal) == true);
            adjust.Focus(); television.Handle(GamepadButtons.Accept);
            await settings.PendingSave;
            Assert.Equal(2, select.SelectedIndex);
            Assert.Equal("store", await repository.GetAsync(GameLinkRouter.SettingKey));
            Assert.True(adjust.IsKeyboardFocusWithin);
            Assert.True(adjust.Bounds.Width > 0);
        }
        finally { window.Close(); tv.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_presentations_surface_browser_fallback(bool fullscreen)
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(GameLinkRouter.SettingKey, "in-app");
        var dispatcher = new UriRecorder();
        var router = new GameLinkRouter(dispatcher, settings, new Clients());
        using var services = new ServiceCollection().AddSingleton<IGameLinkRouter>(router).BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        string? notice = null;
        context.Notice += message => notice = message;
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, steamAppId: "440"), "Never played", [], now, linkRouter: router);
        var view = new GameDetailsView { DataContext = details };
        using var page = new FullscreenDetailsPage(context, details);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var link = Assert.Single(details.Links, candidate => candidate.Label == "Store page");
            if (fullscreen) context.OpenLink(link);
            else
            {
                var more = view.FindControl<Button>("MoreActionsButton")!;
                var menu = Assert.IsType<MenuFlyout>(more.Flyout);
                var row = Assert.Single(menu.Items.OfType<MenuItem>(), item => ReferenceEquals(item.DataContext, link));
                row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(link.Uri, Assert.Single(dispatcher.Opened).ToString());
            if (fullscreen) Assert.Contains("Opened in your browser", notice);
            else
            {
                Assert.Contains("Opened in your browser", details.LinkStatus);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == details.LinkStatus);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_settings_surfaces_report_a_failed_preference_write(bool fullscreen)
    {
        var settings = new ApplicationSettingsViewModel(new UnwritableSettings());
        var shell = new MainWindowViewModel(PreviewData.Library, PreviewData.MergeQueue, PreviewData.Stores,
            PreviewData.Appearance, PreviewData.Feed, PreviewData.AccountStats, PreviewData.LibrarySettings, applicationSettings: settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Application");
        Control control = fullscreen ? page : new ApplicationSettingsView { DataContext = settings };
        var window = new Window { Width = 1920, Height = 1080, Content = control };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (fullscreen)
            {
                var choice = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Open links in");
                choice.Focus(); page.Handle(GamepadButtons.Accept);
            }
            else Assert.Single(control.GetVisualDescendants().OfType<ComboBox>(), choice => AutomationProperties.GetName(choice) == "Open links in").SelectedIndex = 1;
            await settings.PendingSave;
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.HasProblem);
            Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == settings.Problem);
        }
        finally { window.Close(); }
    }

    private sealed class UnwritableSettings : ISettingsRepository
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken ct = default) => throw new IOException("Read-only fixture");
    }
    private sealed class Clients : IStoreClientAvailability { public bool IsAvailable(string scheme) => true; }
    private sealed class UriRecorder : IUriDispatcher
    {
        public List<Uri> Opened { get; } = [];
        public Task<bool> OpenAsync(Uri uri) { Opened.Add(uri); return Task.FromResult(true); }
    }
}
