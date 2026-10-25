using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Electron.Fixtures;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class SteamAccountReadingParityTests
{
    private const string Header = "schema_version,ownership_id,release_id,title,store,acquired_at,license_type,price_paid_cents,price_source,account_ref\r\n";

    [Fact]
    public async Task Exact_three_owner_export_keeps_quoted_multiline_title_zero_unknown_and_read_only_count()
    {
        await using var host = await Host.StartAsync("export");
        var before = await host.State();
        var result = await host.Api.GetAsync<AcquisitionExportResponse>("exports/acquisitions");
        Assert.Equal(3, result.OwnershipCount);
        Assert.Equal(Header
            + "\"2\",\"1\",\"1\",\"A, \"\"game\"\"\r\npart two\",\"steam\",\"2024-02-03T00:00:00.0000000Z\",\"purchase\",\"1299\",\"steam_purchase_history\",\r\n"
            + "\"2\",\"2\",\"1\",\"A, \"\"game\"\"\r\npart two\",\"gog\",,,\"0\",,\r\n"
            + "\"2\",\"3\",\"1\",\"A, \"\"game\"\"\r\npart two\",\"epic\",,,,,\r\n", result.Content);
        Assert.Equal(result, await host.Api.GetAsync<AcquisitionExportResponse>("exports/acquisitions"));
        var after = await host.State();
        Assert.Equal(before.Ownerships, after.Ownerships); Assert.Empty(after.Acquisitions);
        Assert.Empty(after.Transactions); Assert.Empty(after.Licenses); Assert.Empty(after.ProviderRequests);
    }

    [Fact]
    public async Task Empty_export_is_exact_header_count_zero_and_does_not_create_ownerships()
    {
        await using var host = await Host.StartAsync("empty");
        var result = await host.Api.GetAsync<AcquisitionExportResponse>("exports/acquisitions");
        Assert.Equal(new AcquisitionExportResponse(Header, 0), result);
        Assert.Empty((await host.State()).Ownerships);
    }

    [Fact]
    public async Task Original_Alpha_and_Beta_saved_pages_load_and_import_twice_without_duplicate_facts()
    {
        await using var host = await Host.StartAsync("saved");
        var seed = await host.State();
        Assert.Equal(new[] { "first.html", "second.html" }, seed.Files.Select(file => file.Name));
        for (var pass = 0; pass < 2; pass++)
        {
            var loaded = await host.Load(seed.Files);
            Assert.Equal(new[] { "first.html", "second.html" }, loaded.Files.Select(file => file.Path));
            Assert.All(loaded.Files, file => Assert.Equal(SteamAccountPageFileOutcome.Loaded, file.Outcome));
            Assert.Single(loaded.Pages.AdditionalLicensesHtml); Assert.Null(loaded.Pages.SteamId);
            Assert.Equal(SteamAccountPageSource.SavedFile, loaded.Pages.Source);
            var report = await host.Import(loaded.Pages);
            Assert.Equal("Parsed", report.LicensesOutcome); Assert.Equal("Absent", report.HistoryOutcome);
            Assert.Equal(2, report.LicenseRowsParsed); Assert.Equal(pass == 0 ? 2 : 0, report.LicenseFactsRecorded);
            Assert.Equal(pass == 0 ? 0 : 2, report.LicenseFactsAlreadyRecorded); Assert.False(report.LicensesTruncated);
            var state = await host.State();
            Assert.Equal(pass + 1, state.ImportCount);
            Assert.Equal(new[] { "Alpha", "Beta" }, state.Licenses.Select(fact => fact.ItemName).Order());
            Assert.All(state.Licenses, fact =>
            {
                Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), fact.AcquiredAt);
                Assert.Null(fact.AccountRef);
            });
            Assert.Empty(state.Ownerships); Assert.Empty(state.Transactions);
        }
    }

    [Fact]
    public async Task Complete_original_unknown_pages_never_borrow_10001_and_named_10002_cannot_fill_Lantern()
    {
        await using var host = await Host.StartAsync("unknown");
        var seed = await host.State();
        Assert.Equal("10001", seed.ConfirmedAccount); Assert.Equal("own", seed.AccountScope);
        var loaded = await host.Load(seed.Files);
        Assert.Equal(seed.SourcePages!.LicensesHtml, loaded.Pages.LicensesHtml);
        Assert.Equal(seed.SourcePages.HistoryHtml, loaded.Pages.HistoryHtml);
        Assert.Equal(seed.SourcePages.CapturedAt, loaded.Pages.CapturedAt);
        Assert.Null(loaded.Pages.SteamId);
        var first = await host.Import(loaded.Pages);
        Assert.True(first.TransactionFactsRecorded > 0); Assert.True(first.LicenseFactsRecorded > 0);
        var state = await host.State();
        Assert.All(state.Transactions, fact => { Assert.Null(fact.AccountRef); Assert.Equal(seed.SourcePages.CapturedAt.UtcDateTime, fact.CapturedAt); });
        Assert.All(state.Licenses, fact => Assert.Null(fact.AccountRef));
        Assert.Null(Assert.Single(state.Acquisitions).AccountRef);
        var raw = Assert.Single(state.Ownerships); Assert.NotNull(raw.AcquiredAt);
        var own = Assert.Single((await host.Details()).Ownerships);
        Assert.Equal(raw.Id, own.Id); Assert.Null(own.AcquiredAt); Assert.Null(own.PricePaidCents);
        await host.Change("all");
        Assert.NotNull(Assert.Single((await host.Details()).Ownerships).AcquiredAt);
        var named = await host.Import(seed.NamedPages!);
        Assert.Equal(0, named.OwnershipsFilled);
        state = await host.State();
        Assert.Single(state.Acquisitions); Assert.Null(state.Acquisitions[0].AccountRef);
        Assert.Equal(new string?[] { null, "10002" }, state.Transactions.Select(fact => fact.AccountRef).Distinct().Order());
        Assert.Equal(first.TransactionFactsRecorded * 2, state.Transactions.Count);
        Assert.Equal(first.LicenseFactsRecorded * 2, state.Licenses.Count);
        var stats = await host.Api.GetAsync<AccountStats>("statistics/accounts/steam");
        Assert.Equal(1, stats.KnownAccountCount);
        Assert.Equal(first.TransactionFactsRecorded + first.LicenseFactsRecorded, stats.UnknownAccountFactCount);
        Assert.Equal(state.Transactions.Count, stats.TransactionCount); Assert.Equal(state.Licenses.Count, stats.LicenseCount);
        Assert.True(stats.GrossProductSpendCents > 0); // Both presentations withhold these mixed-account amounts.
        Assert.Empty(state.ProviderRequests);
    }

    [Fact]
    public async Task Original_parser_retains_only_explicit_10001_identity_and_never_renders_it_in_diagnostics()
    {
        await using var host = await Host.StartAsync("unknown");
        var explicitPages = SteamAccountReadingFixture.OriginalPages(10001);
        Assert.Equal(explicitPages.SteamId, SteamAccountPageReader.Read(explicitPages).SteamId);
        Assert.Null(SteamAccountPageReader.Read(SteamAccountReadingFixture.OriginalPages(null)).SteamId);
        Assert.DoesNotContain(explicitPages.SteamId!, explicitPages.ToString());
        await host.Import(explicitPages);
        var state = await host.State();
        Assert.NotEmpty(state.Licenses); Assert.NotEmpty(state.Transactions);
        Assert.All(state.Licenses, fact => Assert.Equal("10001", fact.AccountRef));
        Assert.All(state.Transactions, fact => Assert.Equal("10001", fact.AccountRef));
    }

    [Fact]
    public async Task Source_activity_scope_normalizes_000123_and_keeps_failure_and_missing_confirmation_separate()
    {
        await using var host = await Host.StartAsync("activity-scope");
        var response = await host.Activity(1, 99);
        Assert.False(response.AccountConfirmationRequired);
        var call = Assert.Single((await host.State()).ActivityCalls);
        Assert.Equal(new long[] { 1 }, call.OwnershipIds); Assert.Equal("123", call.AccountRef);
        // Original fake repository intentionally over-returns owner99. The production renderer
        // intersects returned rows with its visible scope; the real repository test below never over-returns.
        Assert.Equal(new long[] { 1, 3 }, response.Activity.Select(row => row.Id));
        Assert.Equal(SteamAccountReadingFixture.Row(1), response.Activity[0]);
        await host.Change("fail");
        var failure = await Assert.ThrowsAsync<BackendApiException>(() => host.Activity(1));
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        var state = await host.State();
        Assert.True(state.ActivityCalls.Last().Failed); Assert.Equal("456", state.ActivityCalls.Last().AccountRef);
        await host.Change("missing-confirmation");
        response = await host.Activity(1);
        Assert.True(response.AccountConfirmationRequired); Assert.Empty(response.Activity);
        Assert.Equal(state.ActivityCalls.Count, (await host.State()).ActivityCalls.Count);
    }

    [Fact]
    public async Task Exact_two_source_estimates_cross_HTTP_without_changing_the_recorded_600_second_sitting()
    {
        await using var host = await Host.StartAsync("activity");
        var before = await host.Details();
        Assert.Equal(600, Assert.Single(Assert.Single(before.Sessions).Value).DurationSeconds);
        var result = await host.Activity(1);
        Assert.Equal(new[] { SteamAccountReadingFixture.Row(1), SteamAccountReadingFixture.Row(2) with { ComparisonUnavailable = true, UnexplainedMinutes = null } }, result.Activity);
        var after = await host.Details();
        Assert.Equal(JsonSerializer.Serialize(before.Sessions), JsonSerializer.Serialize(after.Sessions));
        var state = await host.State(); Assert.Equal(1, state.SessionCount); Assert.Equal(600, state.SessionSeconds);
        Assert.Empty(state.ProviderRequests);
    }

    [Fact]
    public async Task Real_SQLite_activity_excludes_hidden_other_account_and_covered_increases_without_creating_sessions()
    {
        await using var host = await Host.StartAsync("activity-real");
        var before = await host.State();
        var result = await host.Activity(1, 2, 3, 4);
        var row = Assert.Single(result.Activity);
        Assert.Equal(1, row.OwnershipId); Assert.Equal("123", row.AccountRef);
        Assert.Equal(31, row.SteamDeltaMinutes); Assert.Equal(31, row.UnexplainedMinutes);
        Assert.Equal(0, row.MatchedRecordedMinutes); Assert.False(row.ComparisonUnavailable);
        Assert.Equal(SteamAccountReadingFixture.Row(1).WindowStartedAt, row.WindowStartedAt);
        Assert.Equal(SteamAccountReadingFixture.Row(1).WindowEndedAt, row.WindowEndedAt);
        var after = await host.State();
        var call = Assert.Single(after.ActivityCalls);
        Assert.DoesNotContain(3, call.OwnershipIds); Assert.Equal("123", call.AccountRef);
        Assert.Equal(before.Sessions, after.Sessions); Assert.Equal(2, after.SessionCount); Assert.Equal(2460, after.SessionSeconds);
        Assert.DoesNotContain((await host.Api.GetAsync<LibraryResponse>("library")).Games, game => game.WorkId == 3);
    }

    [Theory]
    [InlineData("epic")]
    [InlineData("gog")]
    [InlineData("mixed")]
    public async Task Original_non_Steam_and_mixed_populations_preserve_zero_facts_and_the_Steam_scope(string kind)
    {
        await using var host = await Host.StartAsync(kind);
        var game = Assert.Single((await host.Api.GetAsync<LibraryResponse>("library")).Games);
        Assert.Equal("Fixture", game.Title); Assert.Equal(0, game.PlaytimeMinutes); Assert.Null(game.LastPlayedAt);
        Assert.Equal(kind == "mixed" ? new[] { "gog", "steam" } : [kind], game.Entries.Select(entry => entry.Store));
        var ids = game.Entries.Where(entry => entry.Store == "steam").Select(entry => entry.OwnershipId).ToArray();
        Assert.Equal(kind == "mixed" ? new long[] { 2 } : [], ids);
        Assert.Empty((await host.Activity(ids)).Activity);
        Assert.All((await host.Details()).Sessions, entry => Assert.Empty(entry.Value));
    }

    [Fact]
    public async Task Fixture_controls_import_and_export_preserve_real_bearer_authentication()
    {
        await using var host = await Host.StartAsync("empty");
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/steam-account-reading/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/v1/exports/acquisitions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/v1/imports/steam/pages", new { })).StatusCode);
        Assert.Empty((await host.State()).Ownerships);
    }

    private sealed class Host(string directory) : IAsyncDisposable
    {
        private WebApplication _app = null!;
        public HttpClient Http { get; private set; } = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public static async Task<Host> StartAsync(string kind)
        {
            var host = new Host(Path.Combine(Path.GetTempPath(), "winnow-steam-reading-api-" + Guid.NewGuid().ToString("N")));
            try
            {
                host._app = BackendApplication.Build(["--data-dir", host.directory, "--no-sync"], SteamAccountReadingFixture.Register);
                host._app.Services.GetRequiredService<SteamAccountReadingFixture>().Initialize(host.directory);
                SteamAccountReadingFixture.Map(host._app);
                await host._app.StartAsync();
                var endpoint = await BackendConnection.ReadAsync(host.directory);
                host.Http = new HttpClient { BaseAddress = new(endpoint.Address) };
                host.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
                host.Api = WinnowApiClient.Attach(host.directory);
                (await host.Http.PostAsJsonAsync("__fixture/steam-account-reading/seed", new SteamReadingSeed(kind))).EnsureSuccessStatusCode();
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        private readonly string directory = directory;
        public async Task<SteamReadingState> State() => (await Http.GetFromJsonAsync<SteamReadingState>("__fixture/steam-account-reading/state"))!;
        public async Task Change(string stage) => (await Http.PostAsJsonAsync("__fixture/steam-account-reading/change", new SteamReadingChange(stage))).EnsureSuccessStatusCode();
        public Task<GameDetailsResponse> Details() => Api.GetAsync<GameDetailsResponse>("games/1/details");
        public Task<SteamActivityResponse> Activity(params long[] ids) => Api.SendAsync<SteamActivityRequest, SteamActivityResponse>(HttpMethod.Post, "activity/steam", new(ids));
        public Task<SteamImportResponse> Import(SteamAccountPages pages) => Api.SendAsync<SteamAccountPages, SteamImportResponse>(HttpMethod.Post, "imports/steam/pages", pages);
        public Task<SteamAccountPageLoadResult> Load(IReadOnlyList<SteamReadingFile> files) => Api.SendAsync<SteamPageUploadRequest, SteamAccountPageLoadResult>(HttpMethod.Post,
            "imports/steam/load-files", new(files.Select(file => new SteamPageUpload(file.Name, Encoding.UTF8.GetBytes(file.Content))).ToArray()));
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose(); Http?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
