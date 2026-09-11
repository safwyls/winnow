using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class StoresAccountContextTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacing_key_clears_old_confirmation_and_disables_account_scope_on_both_surfaces(bool fullscreen)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var settings = new SettingsRepository(db.Factory);
        var key = new Keys();
        var credentials = new SteamCredentialProvider(key, TimeProvider.System);
        var confirmation = new SteamAccountConfirmation(settings, keys: key, unitOfWork: db.Factory);
        Assert.True(await confirmation.ConfirmAsync(SteamId.FromAccountId(10001)!.Value, SteamAccountConfirmationSource.WebApiKey));
        var connections = new StoreConnections(steamCredentials: credentials, apiKeyStore: key, confirmation: confirmation);
        var stores = new StoresViewModel(connections, accountVisibility: new AccountVisibilityService(settings, new LibraryQueryRepository(db.Factory)));
        using var context = Context(stores);
        using var page = new FullscreenPlatformPage(context, "Steam");
        var view = new StoresView { DataContext = stores };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : view };
        try
        {
            await stores.RefreshCommand.ExecuteAsync(null);
            window.Show(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            var toggle = Assert.Single(window.GetVisualDescendants().OfType<Control>(), control =>
                control.IsEffectivelyVisible && AutomationProperties.GetName(control) == stores.AccountScopeToggleLabel);
            Assert.True(toggle.IsEnabled);
            stores.SteamApiKeyInput = "replacement-test-key";
            await stores.SaveSteamApiKeyCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await confirmation.GetConfirmedAccountRefAsync());
            Assert.True(stores.SteamHasApiKey);
            Assert.False(stores.SteamAccountConfirmed);
            Assert.False(toggle.IsEnabled);
            if (!fullscreen) { stores.OpenAccountsModalCommand.Execute(null); Dispatcher.UIThread.RunJobs(); }
            AssertText(window, stores.AccountScopeBlockedMessage);
            await stores.ClearSteamApiKeyCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(stores.SteamHasApiKey);
            Assert.False(toggle.IsEnabled);
            AssertText(window, stores.AccountScopeBlockedMessage);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Epic_signout_and_replacement_signin_render_only_current_account(bool fullscreen)
    {
        var connections = new Connections { Epic = new(true, "Account A") };
        var stores = new StoresViewModel(connections) { SelectedPlatform = StorePlatform.Epic };
        using var context = Context(stores);
        using var page = new FullscreenPlatformPage(context, "Epic");
        var view = new StoresView { DataContext = stores };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : view };
        try
        {
            await stores.RefreshCommand.ExecuteAsync(null);
            window.Show(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            AssertText(window, "Account A");
            await stores.SignOutOfEpicCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            AssertText(window, stores.EpicStatusLabel);
            AssertNoText(window, "Account A");
            await stores.SignInToEpicCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            AssertText(window, "Account B");
            AssertNoText(window, "Account A");
            Assert.True(stores.EpicIsSignedIn);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
                (ReferenceEquals(button.Command, stores.SignOutOfEpicCommand) || AutomationProperties.GetName(button) == "Sign out of Epic"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "epic")]
    [InlineData(true, "epic")]
    [InlineData(false, "gog")]
    [InlineData(true, "gog")]
    public async Task Persisted_unknown_install_observation_keeps_play_until_authoritative_absence(bool fullscreen, string store)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            UPDATE ownerships SET store=@store, installed=1, install_path='C:\Fixture\Game' WHERE id=1;
            UPDATE external_ids SET provider=@store, provider_id=@providerId WHERE release_id=1;
            """, new { store, providerId = store == "gog" ? "12345" : "sample-item" });
        var owners = new OwnershipRepository(db.Factory);
        using var library = Library(db, new LaunchKeys());
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 1));
        var details = library.Details!;
        using var context = Context(new StoresViewModel(new Connections()), library);
        using var page = new FullscreenDetailsPage(context, details);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            AssertPrimary("Play");
            await owners.UpsertAsync(new(1, store, null, null, null, null));
            await library.LoadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            AssertPrimary("Play");
            Assert.Equal("C:\\Fixture\\Game", (await owners.GetAsync(1))!.InstallPath);
            await owners.UpsertAsync(new(1, store, null, null, null, false));
            await library.LoadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            AssertPrimary("Install");
            Assert.False(details.Tile.IsOnDisk);
            Assert.Null((await owners.GetAsync(1))!.InstallPath);
        }
        finally { window.Close(); }

        void AssertPrimary(string label)
        {
            Assert.Same(details, library.Details);
            Assert.Equal(label, details.PrimaryAction!.Label);
            var button = fullscreen
                ? Assert.Single(window.GetVisualDescendants().OfType<Button>(), item => AutomationProperties.GetAutomationId(item) == "details-primary-action")
                : view.FindControl<Button>("LaunchButton")!;
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == label);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_render_selected_account_acquisition_then_withhold_conflicting_aggregate_license(bool fullscreen)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var settings = new SettingsRepository(db.Factory);
        var acquisitions = new AccountAcquisitionRepository(db.Factory);
        var observation = new OwnershipAcquisitionObservation { OwnershipId = 1, AccountRef = "10001",
            AcquiredAt = new DateTime(2020, 1, 2, 12, 0, 0, DateTimeKind.Utc), LicenseType = "retail", Source = "steam", CapturedAt = DateTime.UtcNow };
        await acquisitions.TryAppendAsync(observation);
        await acquisitions.TryAppendAsync(observation with { AccountRef = "10002", AcquiredAt = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc), LicenseType = "gift" });
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10002");
        using var library = Library(db, acquisition: new AccountAcquisitionReader(acquisitions, settings));
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(tile => tile.OwnershipId == 1));
        var details = library.Details!;
        details.SelectedTabIndex = 4;
        using var context = Context(new StoresViewModel(new Connections()), library);
        using var page = new FullscreenDetailsPage(context, details, selectedSection: 3);
        var window = new Window { Width = 1920, Height = 1080,
            Content = fullscreen ? page : new GameDetailsView { DataContext = details } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("2024", details.Acquisition!.DateText);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains(details.Acquisition.DateText, StringComparison.Ordinal) == true);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains(details.Acquisition.LicenseText, StringComparison.Ordinal) == true);
            var gift = details.Acquisition.LicenseText;
            Assert.NotEmpty(gift);
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.All);
            await library.LoadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Contains("2020", details.Acquisition!.DateText);
            Assert.False(details.Acquisition.HasLicence);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains(gift, StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Statistics_render_known_and_unknown_account_scope_without_ambiguous_money_totals(bool fullscreen)
    {
        using var db = new TempDatabase();
        var facts = new AccountFactRepository(db.Factory);
        var fact = new AccountTransactionFact { Source = "steam", AccountRef = "10001", Kind = "purchase",
            TransactionTypeRaw = "Purchase", ItemNames = ["Fixture game"], OccurredAt = DateTime.UtcNow,
            TotalCents = 500, CurrencySymbol = "$", CapturedAt = DateTime.UtcNow };
        await facts.TryAppendAsync(fact);
        await facts.TryAppendAsync(fact with { AccountRef = "10002" });
        await facts.TryAppendAsync(fact with { AccountRef = null });
        var repository = new AccountStatsRepository(db.Factory);
        var model = new AccountStatsViewModel(repository);
        await model.RefreshCommand.ExecuteAsync(null);
        using var services = new ServiceCollection().AddSingleton<IAccountStatsRepository>(repository).BuildServiceProvider();
        using var context = Context(new StoresViewModel(new Connections()), services: services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var window = new Window { Width = 1920, Height = 1080,
            Content = fullscreen ? page : new AccountStatsView { DataContext = model } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (fullscreen) { await page.PendingRefresh; Dispatcher.UIThread.RunJobs(); }
            Assert.Contains("2 identified", model.IntroMessage);
            Assert.Contains("unknown account", model.IntroMessage);
            AssertText(window, model.IntroMessage);
            Assert.All(model.SpendRows, row => Assert.Empty(row.AmountText));
            AssertNoText(window, "$15.00");
        }
        finally { window.Close(); }
    }

    private static LibraryViewModel Library(TempDatabase db, IEpicLaunchKeys? keys = null, IAccountAcquisitionReader? acquisition = null)
        => new(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory), new ReleaseRepository(db.Factory),
            new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory), epicLaunchKeys: keys, acquisitionReader: acquisition);
    private static FullscreenContext Context(StoresViewModel stores, LibraryViewModel? library = null, IServiceProvider? services = null)
    {
        library ??= new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(library, preview.MergeQueue, stores, preview.Appearance, feed, preview.AccountStats, preview.LibrarySettings);
        return new(library, feed, shell, services);
    }
    private static void AssertText(Control parent, string text) => Assert.Contains(parent.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == text);
    private static void AssertNoText(Control parent, string text) => Assert.DoesNotContain(parent.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == text);

    private sealed class LaunchKeys : IEpicLaunchKeys
    {
        public Task<IReadOnlyDictionary<string, EpicLaunchKey>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, EpicLaunchKey>>(new Dictionary<string, EpicLaunchKey> { ["sample-item"] = EpicLaunchKey.Create("sample-namespace", "sample-item", "FixtureGame")!.Value });
    }
    private sealed class Keys : ISteamApiKeyProvider, ISteamApiKeyStore
    {
        private string? _key = "original-test-key";
        public ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult(SteamApiKey.TryCreate(_key, SettingsTableApiKeySource.SourceName));
        ValueTask<string?> ISteamApiKeyStore.GetAsync(CancellationToken ct) => ValueTask.FromResult(_key);
        public void Invalidate() { }
        public Task<SteamApiKeySaveOutcome> SaveAsync(string key, CancellationToken ct = default) { _key = key; return Task.FromResult(SteamApiKeySaveOutcome.Stored); }
        public Task ClearAsync(CancellationToken ct = default) { _key = null; return Task.CompletedTask; }
    }
    private sealed class Connections : IStoreConnections
    {
        public StoreSession? Epic { get; set; }
        public ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default) => ValueTask.FromResult(SteamConnection.None);
        public Task<SteamApiKeySaveOutcome> SaveSteamApiKeyAsync(string? key, CancellationToken ct = default) => Task.FromResult(SteamApiKeySaveOutcome.Refused);
        public Task ClearSteamApiKeyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default) => ValueTask.FromResult(Epic);
        public Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default) { Epic = new(true, "Account B"); return Task.FromResult(new StoreSignInOutcome(true, "Account B", true, StoreSignInProblem.None, "")); }
        public Task SignOutOfEpicAsync(CancellationToken ct = default) { Epic = null; return Task.CompletedTask; }
    }
}
