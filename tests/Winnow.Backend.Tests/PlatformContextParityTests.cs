using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Electron.Fixtures;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Epic.Web.Auth;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class PlatformContextParityTests
{
    [Fact]
    public async Task Original_empty_completed_years_ask_only_2026_and_publish_an_unconfirmed_account_visibility()
    {
        await using var host = await Host.StartAsync(null);
        var database = host.App.Services.GetRequiredService<ISqliteConnectionFactory>();
        var settings = new SettingsRepository(database);
        var releases = new ReleaseRepository(database);
        var ownerships = new OwnershipRepository(database);
        var workId = await new WorkRepository(database).InsertAsync(new Work { Name = "Portal" });
        var releaseId = await releases.InsertAsync(new Release { WorkId = workId, Name = "Portal" });
        await releases.AddExternalIdAsync(new ExternalId { ReleaseId = releaseId, Provider = "steam", ProviderId = "400" });
        await ownerships.InsertAsync(new Ownership { ReleaseId = releaseId, Store = "steam", AccountRef = "11111" });
        var account = SteamId.FromAccountId(11111)!.Value;
        for (var year = 2022; year < 2026; year++)
            await settings.SetAsync($"steam.backfill.yir.{account.Value}.{year}",
                "2026-01-01T00:00:00.0000000Z;games=0;written=0");
        await settings.SetAsync($"steam.backfill.account.{account.Value}.confirmed", "2026-01-01T00:00:00.0000000Z");
        var keys = host.App.Services.GetRequiredService<PlatformKeys>();
        await keys.SaveAsync("the-one-key");
        var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var history = new EmptyHistory(SteamCredentialIdentity.From(SteamCredential.FromApiKey(await keys.GetAsync()))!, now);
        var backfill = new SteamPlaytimeBackfillService(history, releases, ownerships, new OwnershipAccountRepository(database),
            new PlayRecordRepository(database), new PlaytimeSnapshotRepository(database), settings, database, new LibrarySyncGate(),
            new SteamPlaytimeBackfillOptions { FirstYear = 2022 }, new FixedClock(now), keys, NullLogger<SteamPlaytimeBackfillService>.Instance);
        await backfill.BackfillAsync();
        Assert.Equal(new[] { (account, 2026) }, history.Asked);
        Assert.Null(SteamOwnedAccount.Clean(await settings.GetAsync(SteamOwnedAccount.RefSettingKey)));
        Assert.False((await host.Visibility()).AccountConfirmed);
        Assert.True((await host.Connections()).Steam.HasApiKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_and_incomplete_source_inventories_drive_both_library_and_real_feed_and_a_failed_attempt_expands_them(bool complete)
    {
        await using var host = await Host.StartAsync(new("inventory", Complete: complete));
        await AssertScope(complete ? 1 : 2);
        if (!complete) { await host.Change("complete"); await AssertScope(1); }
        await host.Change("failed");
        await AssertScope(2);
        Assert.Equal(0, (await host.Visibility()).HiddenCount);
        var state = await host.State();
        Assert.Equal("111", state.GetProperty("confirmedAccount").GetString());
        Assert.Equal(0, state.GetProperty("inventories")[0].GetProperty("is_complete").GetInt32());

        async Task AssertScope(int count)
        {
            var library = await host.Api.GetAsync<LibraryResponse>("library");
            var feed = await host.Api.GetAsync<FeedSnapshot>("feed");
            Assert.Equal(count, library.Games.Count);
            var items = feed.Shelves.SelectMany(shelf => shelf.Items).ToArray();
            Assert.Equal(count, items.Length);
            Assert.Contains(items, item => item.Title == "My game");
            Assert.Equal(count == 2, items.Any(item => item.Title == "Household game"));
            Assert.False(feed.Failed);
        }
    }

    [Fact]
    public async Task Original_key_confirmation_10001_is_cleared_before_replacement_publication_and_clear_preserves_disabled_scope()
    {
        await using var host = await Host.StartAsync(new("key"));
        Assert.True((await host.Visibility()).AccountConfirmed);
        Assert.Equal("10001", (await host.State()).GetProperty("confirmedAccount").GetString());
        Assert.True((await host.Connections()).Steam.HasApiKey);
        var observations = new List<string?>();
        host.App.Services.GetRequiredService<OwnershipRefreshRequests>().Requested += () =>
            observations.Add(host.App.Services.GetRequiredService<ISteamAccountConfirmation>().GetConfirmedAccountRefAsync().GetAwaiter().GetResult());
        Assert.Equal(SteamApiKeySaveOutcome.Stored, await host.Api.SendAsync<SaveSteamApiKey, SteamApiKeySaveOutcome>(HttpMethod.Put,
            "connections/stores/steam/key", new("replacement-test-key")));
        Assert.Null(await host.App.Services.GetRequiredService<ISteamAccountConfirmation>().GetConfirmedAccountRefAsync());
        Assert.True((await host.Connections()).Steam.HasApiKey);
        Assert.False((await host.Visibility()).AccountConfirmed);
        Assert.Equal(new string?[] { null }, observations);
        Assert.Equal("replacement-test-key", (await host.App.Services.GetRequiredService<ISteamApiKeyProvider>().GetAsync())!.Value);
        await host.Api.SendAsync<SaveSteamApiKey, SteamApiKeySaveOutcome>(HttpMethod.Put, "connections/stores/steam/key", new(null));
        Assert.False((await host.Connections()).Steam.HasApiKey);
        Assert.False((await host.Visibility()).AccountConfirmed);
        Assert.Equal(new string?[] { null, null }, observations);
        Assert.DoesNotContain("replacement-test-key", await host.Http.GetStringAsync("api/v1/connections/stores"));
    }

    [Theory]
    [InlineData("epic", "sample-item")]
    [InlineData("gog", "12345")]
    public async Task Unknown_install_observation_preserves_same_ownership_and_path_until_authoritative_absence(string store, string providerId)
    {
        await using var host = await Host.StartAsync(new("install", store));
        var original = Assert.Single((await host.Details()).Ownerships, owner => owner.Id == 1);
        Assert.True(original.Installed);
        Assert.Equal("C:\\Fixture\\Game", original.InstallPath);
        Assert.Equal(store, original.Store);
        var workspace = await host.Api.GetAsync<LibraryWorkspaceResponse>("library/workspace");
        Assert.Contains(workspace.ExternalIds, id => id.ReleaseId == 1 && id.Provider == store && id.ProviderId == providerId);
        if (store == "epic") Assert.Equal("FixtureGame", workspace.EpicLaunchKeys[providerId].ArtifactId);
        await host.Change("unknown");
        Assert.Equal(original, Assert.Single((await host.Details()).Ownerships, owner => owner.Id == 1));
        Assert.True(Assert.Single((await host.Api.GetAsync<LibraryResponse>("library")).Games, game => game.WorkId == 1).Entries[0].Installed);
        await host.Change("absent");
        var absent = Assert.Single((await host.Details()).Ownerships, owner => owner.Id == 1);
        Assert.Equal(original.Id, absent.Id);
        Assert.Equal(original.ReleaseId, absent.ReleaseId);
        Assert.False(absent.Installed);
        Assert.Null(absent.InstallPath);
        Assert.False(Assert.Single((await host.Api.GetAsync<LibraryResponse>("library")).Games, game => game.WorkId == 1).Entries[0].Installed);
    }

    [Fact]
    public async Task Selected_account_acquisition_is_gift_2024_then_all_accounts_choose_2020_and_withhold_conflicting_licence()
    {
        await using var host = await Host.StartAsync(new("acquisition"));
        var selected = Assert.Single((await host.Details()).Ownerships, owner => owner.Id == 1);
        Assert.Equal(new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc), selected.AcquiredAt);
        Assert.Equal("gift", selected.LicenseType);
        Assert.Equal(0, selected.PricePaidCents);
        await host.Change("all");
        var aggregate = Assert.Single((await host.Details()).Ownerships, owner => owner.Id == 1);
        Assert.Equal(new DateTime(2020, 1, 2, 12, 0, 0, DateTimeKind.Utc), aggregate.AcquiredAt);
        Assert.Null(aggregate.LicenseType);
        Assert.Equal(selected.Id, aggregate.Id);
    }

    [Fact]
    public async Task Three_original_five_dollar_facts_preserve_two_known_accounts_and_unknown_scope_for_safe_presentation()
    {
        await using var host = await Host.StartAsync(new("acquisition"));
        var stats = await host.Api.GetAsync<AccountStats>("statistics/accounts/steam");
        Assert.Equal(2, stats.KnownAccountCount);
        Assert.Equal(1, stats.UnknownAccountFactCount);
        Assert.Equal(3, stats.TransactionCount);
        Assert.Equal(1500, stats.GrossProductSpendCents);
        Assert.Equal(new AccountSpendSlice(3, 1500), stats.Purchases);
        Assert.Equal("$", stats.CurrencySymbol);
        Assert.Single(stats.CurrencyGroups);
        Assert.Equal(2, stats.CurrencyGroups[0].KnownAccountCount);
        Assert.Equal(1, stats.CurrencyGroups[0].UnknownAccountFactCount);
        // The API retains observed monetary facts. Both UI surfaces must withhold their ambiguous aggregate.
    }

    [Fact]
    public async Task Real_connection_projection_tracks_live_expired_and_absent_Steam_session_without_network_or_identity_leakage()
    {
        await using var host = await Host.StartAsync(new("platforms"));
        await host.Change("steam-live");
        var live = await host.Connections();
        Assert.True(live.Steam.HasSession);
        Assert.True(live.Steam.SessionUsable);
        Assert.False(live.Steam.HasApiKey);
        Assert.Equal("76561198000000000", live.Steam.SessionAccount);
        Assert.Equal(SteamSessionHealth.Live, live.SteamHealth);
        await host.Change("steam-expired");
        var expired = await host.Connections();
        Assert.True(expired.Steam.HasSession);
        Assert.False(expired.Steam.SessionUsable);
        Assert.Equal(live.Steam.SessionAccount, expired.Steam.SessionAccount);
        Assert.Equal(SteamSessionHealth.Expired, expired.SteamHealth);
        await host.Change("steam-none");
        Assert.Equal(SteamConnection.None, (await host.Connections()).Steam);
    }

    [Fact]
    public async Task Actual_Epic_attempt_cancel_preserves_expired_identity_and_success_after_signout_replaces_A_with_B()
    {
        await using var host = await Host.StartAsync(new("platforms"));
        Assert.Equal(new StoreSession(true, "Account A"), (await host.Connections()).Epic);
        await host.Api.SendAsync<object?>(HttpMethod.Post, "connections/stores/epic/sign-out", null);
        Assert.Null((await host.Connections()).Epic);
        var client = Guid.NewGuid().ToString("N");
        var challenge = await host.Api.SendAsync<PluginSignInRequest, EpicAuthChallenge>(HttpMethod.Post, "connections/stores/epic/sign-in", new(client));
        Assert.Equal("https://localhost/launcher/authorized", challenge.Request.RedirectUrl!.AbsoluteUri);
        var result = await host.Api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post,
            "connections/stores/epic/sign-in/complete", new(client, challenge.AttemptId, "fixture-code", AuthCodeKind.AuthorizationCode, challenge.Request.ExpectedState));
        Assert.True(result.Succeeded);
        Assert.Equal(new StoreSession(true, "Account B"), (await host.Connections()).Epic);
        await host.Change("epic-expired");
        Assert.Equal(new StoreSession(false, "Test player"), (await host.Connections()).Epic);
        var cancelled = await host.Api.SendAsync<PluginSignInRequest, EpicAuthChallenge>(HttpMethod.Post, "connections/stores/epic/sign-in", new(client));
        await host.Api.SendAsync(HttpMethod.Post, "connections/stores/sign-in/cancel", new StoreAuthCancel(client, cancelled.AttemptId));
        var failure = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post,
            "connections/stores/epic/sign-in/complete", new(client, cancelled.AttemptId, "fixture-code", AuthCodeKind.AuthorizationCode, cancelled.Request.ExpectedState)));
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.Equal(new StoreSession(false, "Test player"), (await host.Connections()).Epic);
        Assert.Equal(1, host.App.Services.GetRequiredService<PlatformEpicSession>().Exchanges);
    }

    [Fact]
    public async Task Platform_fixture_controls_require_real_backend_authentication()
    {
        await using var host = await Host.StartAsync(new("platforms"));
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/platform-context/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("__fixture/platform-context/change", new PlatformChange("steam-live"))).StatusCode);
    }

    private sealed class Host(string directory, WebApplication app, HttpClient http, WinnowApiClient api) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Http => http;
        public WinnowApiClient Api => api;
        public static async Task<Host> StartAsync(PlatformSeed? seed)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-platform-context-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], PlatformContextFixture.Register);
            PlatformContextFixture.Map(app);
            await app.StartAsync();
            var endpoint = await BackendConnection.ReadAsync(directory);
            var http = new HttpClient { BaseAddress = new(endpoint.Address) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
            var host = new Host(directory, app, http, WinnowApiClient.Attach(directory));
            try
            {
                if (seed is not null) (await http.PostAsJsonAsync("__fixture/platform-context/seed", seed)).EnsureSuccessStatusCode();
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        public async Task Change(string stage) => (await http.PostAsJsonAsync("__fixture/platform-context/change", new PlatformChange(stage))).EnsureSuccessStatusCode();
        public Task<JsonElement> State() => http.GetFromJsonAsync<JsonElement>("__fixture/platform-context/state");
        public Task<StoreConnectionSnapshot> Connections() => api.GetAsync<StoreConnectionSnapshot>("connections/stores");
        public Task<AccountVisibilityState> Visibility() => api.GetAsync<AccountVisibilityState>("connections/account-visibility");
        public Task<GameDetailsResponse> Details() => api.GetAsync<GameDetailsResponse>("games/1/details");
        public async ValueTask DisposeAsync()
        {
            api.Dispose(); http.Dispose(); await app.StopAsync(); await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class EmptyHistory(SteamCredentialIdentity identity, DateTime now) : ISteamHistoryClient
    {
        public List<(SteamId Account, int Year)> Asked { get; } = [];
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public ValueTask<bool> IsCurrentAsync(SteamCredentialIdentity observed,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, CancellationToken ct = default)
            => ValueTask.FromResult(observed == identity);
        public Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(new SteamLastPlayedTimes(true, [], now, false) { CredentialIdentity = identity });
        public Task<SteamYearInReview> GetYearInReviewAsync(SteamId account, int year,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            Asked.Add((account, year));
            return Task.FromResult(new SteamYearInReview(account, year, true, null, [], now, false) { CredentialIdentity = identity });
        }
    }
}
