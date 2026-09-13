using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Reading;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class GameLinkRouterTests
{
    [Theory]
    [InlineData("in-app", true, false, "https://store.steampowered.com/news/app/440", "embedded", false)]
    [InlineData("browser", true, true, "https://store.steampowered.com/news/app/440", "https", false)]
    [InlineData("store", true, true, "https://store.steampowered.com/app/440/", "steam", false)]
    [InlineData("store", true, false, "https://store.steampowered.com/app/440/", "https", true)]
    [InlineData("store", true, true, "https://store.steampowered.com/news/app/440", "https", true)]
    [InlineData("in-app", false, true, "https://store.steampowered.com/news/app/440", "https", true)]
    [InlineData("in-app", true, true, "https://example.com/news", "https", true)]
    [InlineData("in-app", true, true, "https://store.steampowered.com/app/440/", "https", true)]
    [InlineData("browser", true, false, "goggalaxy://openGameView/gog_1", "goggalaxy", false)]
    public async Task Selects_supported_destinations_and_reports_fallbacks(string preference, bool embedded, bool client,
        string url, string scheme, bool fallback)
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(GameLinkRouter.SettingKey, preference);
        var dispatcher = new Dispatcher();
        var reader = new Reader(embedded);
        var router = new GameLinkRouter(dispatcher, settings, new Clients(client), reader);
        var result = await router.OpenAsync(GameLink.Create("Read", url)!, "A game");
        Assert.True(result.Opened);
        Assert.Equal(fallback, result.Message is not null);
        if (scheme == "embedded") { Assert.Equal(1, reader.Calls); Assert.Empty(dispatcher.Uris); }
        else { Assert.Equal(scheme, Assert.Single(dispatcher.Uris).Scheme); Assert.Equal(0, reader.Calls); }
    }

    [Theory]
    [InlineData("https://store.steampowered.com.example.com/app/440/")]
    [InlineData("https://store.steampowered.com:8443/app/440/")]
    [InlineData("http://store.steampowered.com/app/440/")]
    [InlineData("https://user@store.steampowered.com/app/440/")]
    [InlineData("https://store.steampowered.com/app/440/?target=run")]
    [InlineData("https://store.steampowered.com/app/440/#target")]
    [InlineData("https://store.steampowered.com/app/440%2f123/")]
    public void Store_conversion_rejects_noncanonical_addresses(string url)
        => Assert.Null(GameLinkRouter.SteamStoreTarget(new Uri(url)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_or_throwing_store_handler_falls_back_to_browser(bool throws)
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(GameLinkRouter.SettingKey, "store");
        var dispatcher = new Dispatcher { FailScheme = "steam", Throw = throws };
        var router = new GameLinkRouter(dispatcher, settings, new Clients(true));
        var result = await router.OpenAsync(GameLink.Create("Store", "https://store.steampowered.com/app/440/")!, "Game");
        Assert.True(result.Opened);
        Assert.Contains("browser", result.Message);
        Assert.Equal(new[] { "steam", "https" }, dispatcher.Uris.Select(u => u.Scheme));
    }

    [Fact]
    public async Task Unavailable_choices_are_not_offered_and_preference_survives_reload()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var first = new ApplicationSettingsViewModel(settings, storeClients: new Clients(true));
        await first.LoadAsync();
        first.LinkDestinationIndex = 2;
        await first.PendingSave;
        Assert.Equal("store", await settings.GetAsync(GameLinkRouter.SettingKey));
        var second = new ApplicationSettingsViewModel(settings, storeClients: new Clients(true));
        await second.LoadAsync();
        Assert.Equal(2, second.LinkDestinationIndex);
        var unavailable = new ApplicationSettingsViewModel(settings, storeClients: new Clients(false));
        await unavailable.LoadAsync();
        Assert.Equal(new[] { "In Winnow", "System browser" }, unavailable.LinkDestinationOptions);
        Assert.Equal(1, unavailable.LinkDestinationIndex);
        Assert.Equal("store", await settings.GetAsync(GameLinkRouter.SettingKey));
    }

    [Fact]
    public async Task Browser_failure_is_not_reported_as_successful_fallback()
    {
        using var db = new TempDatabase();
        var router = new GameLinkRouter(new Dispatcher { FailScheme = "https" }, new SettingsRepository(db.Factory), new Clients(false));
        var result = await router.OpenAsync(GameLink.Create("Read", "https://example.com/")!, "Game");
        Assert.False(result.Opened);
        Assert.DoesNotContain("Opened", result.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reader_refusal_or_failure_preserves_browser_fallback(bool throws)
    {
        using var db = new TempDatabase();
        var dispatcher = new Dispatcher();
        var reader = new Reader(true) { Refuse = true, Throw = throws };
        var router = new GameLinkRouter(dispatcher, new SettingsRepository(db.Factory), new Clients(false), reader);
        var result = await router.OpenAsync(GameLink.Create("Notes", "https://store.steampowered.com/news/app/440")!, "Game");
        Assert.True(result.Opened);
        Assert.Contains("Opened in your browser", result.Message);
        Assert.Equal(1, reader.Calls);
        Assert.Equal("https", Assert.Single(dispatcher.Uris).Scheme);
    }

    private sealed class Clients(bool available) : IStoreClientAvailability
    {
        public bool IsAvailable(string scheme) => available;
    }
    private sealed class Reader(bool available) : IPatchNotesReader
    {
        public int Calls { get; private set; }
        public bool Refuse { get; init; }
        public bool Throw { get; init; }
        public bool IsAvailable => available;
        public PatchNotesOutcome Open(Uri url, string title)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException();
            return Refuse ? PatchNotesOutcome.Unavailable : PatchNotesOutcome.Opened;
        }
    }
    private sealed class Dispatcher : IUriDispatcher
    {
        public List<Uri> Uris { get; } = [];
        public string? FailScheme { get; init; }
        public bool Throw { get; init; }
        public Task<bool> OpenAsync(Uri uri)
        {
            Uris.Add(uri);
            if (uri.Scheme == FailScheme && Throw) throw new InvalidOperationException();
            return Task.FromResult(uri.Scheme != FailScheme);
        }
    }
}
