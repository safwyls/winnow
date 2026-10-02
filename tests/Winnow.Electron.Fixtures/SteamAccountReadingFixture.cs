using System.Collections.Concurrent;
using System.Net;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Winnow.App.Services;
using Winnow.Application;
using Winnow.Core.Auth;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.SteamWeb;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.Electron.Fixtures;

// Original parser pages and repository-shaped activity rows cross the normal authenticated API.
// Only this separate test executable exposes these controls; no renderer response is rewritten.
internal sealed class SteamAccountReadingFixture(ISqliteConnectionFactory database,
    LibraryChangePublisher publisher, IApplicationChangePublisher changes,
    SteamReadingObservations observations, SteamReadingOfflineHttp offline)
{
    public static readonly DateTime SourceNow = new(2026, 9, 14, 20, 0, 0, DateTimeKind.Utc);
    private string? _kind;
    private string _directory = "";
    private IReadOnlyList<SteamReadingFile> _files = [];
    private int _imports;

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<SteamAccountReadingFixture>();
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, OwnershipNoCredentials>();
        services.AddSingleton<SteamReadingObservations>();
        services.AddSingleton<ISteamPlaytimeObservationRepository>(provider => provider.GetRequiredService<SteamReadingObservations>());
        services.AddSingleton<ISteamAccountPageFileLoader>(_ => new SteamAccountPageFileLoader(clock: new SteamReadingCaptureClock()));
        services.AddSingleton<SteamReadingOfflineHttp>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(provider => provider.GetRequiredService<SteamReadingOfflineHttp>());
    }

    public void Initialize(string directory) => _directory = directory;

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<SteamAccountReadingFixture>();
        app.Use(async (context, next) =>
        {
            await next(context);
            if (context.Request.Method == "POST" && context.Request.Path == "/api/v1/imports/steam/pages"
                && context.Response.StatusCode is >= 200 and < 300)
                Interlocked.Increment(ref fixture._imports);
        });
        app.MapPost("/__fixture/steam-account-reading/seed", (SteamReadingSeed input) => fixture.SeedAsync(input.Kind));
        app.MapPost("/__fixture/steam-account-reading/change", async (SteamReadingChange input) =>
        {
            await fixture.ChangeAsync(input.Stage);
            return Results.NoContent();
        });
        app.MapGet("/__fixture/steam-account-reading/state", () => fixture.StateAsync());
    }

    public async Task<SteamReadingState> SeedAsync(string kind)
    {
        if (_kind is not null) throw new InvalidOperationException("Seed the reading fixture only once.");
        if (kind is not ("export" or "empty" or "saved" or "unknown" or "activity" or "activity-scope" or "activity-real" or "epic" or "gog" or "mixed"))
            throw new ArgumentException("Unknown Steam account reading fixture kind.");
        var works = new WorkRepository(database);
        var releases = new ReleaseRepository(database);
        var owned = new OwnershipRepository(database);
        var settings = new SettingsRepository(database);
        if (kind == "export")
        {
            var work = await works.InsertAsync(new Work { Name = "A, \"game\"\r\npart two" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Store title" });
            await owned.InsertAsync(new Ownership
            {
                ReleaseId = release, Store = "steam", AcquiredAt = new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc),
                LicenseType = "purchase", PricePaidCents = 1299, PriceSource = "steam_purchase_history",
            });
            await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "gog", PricePaidCents = 0 });
            await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "epic" });
        }
        else if (kind == "unknown")
        {
            var work = await works.InsertAsync(new Work { Name = "Lantern Hollow" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Lantern Hollow" });
            await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "steam", AccountRef = "10001" });
            await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10001");
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        }
        else if (kind.StartsWith("activity", StringComparison.Ordinal))
        {
            await SeedActivityAsync(kind);
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
            await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "000123");
        }
        else if (kind is "epic" or "gog" or "mixed")
        {
            var work = await works.InsertAsync(new Work { Name = "Fixture" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Fixture" });
            await owned.InsertAsync(new Ownership { ReleaseId = release, Store = kind == "mixed" ? "gog" : kind });
            if (kind == "mixed") await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        }
        Directory.CreateDirectory(Path.Combine(_directory, "docs"));
        if (kind == "saved")
            _files = [await WriteAsync("first.html", Page("Alpha", 1)), await WriteAsync("second.html", Page("Beta", 2))];
        else if (kind == "unknown")
        {
            var pages = OriginalPages(null);
            _files = [await WriteAsync("licenses.html", pages.LicensesHtml!), await WriteAsync("history.html", pages.HistoryHtml!)];
        }
        _kind = kind;
        await PublishAsync();
        return await StateAsync();
    }

    private async Task SeedActivityAsync(string kind)
    {
        using (var connection = database.Open())
        {
            connection.Execute("""
                INSERT INTO works(id,name) VALUES(1,'Dragonwilds');
                INSERT INTO releases(id,work_id,name) VALUES(1,1,'Dragonwilds');
                INSERT INTO ownerships(id,release_id,store,account_ref,installed) VALUES(1,1,'steam','123',0);
                INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
                    VALUES(1,'123','steam_local','2026-09-01','2026-09-14');
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','1');
                """);
        }
        await new SessionRepository(database).InsertAsync(new Session
        {
            OwnershipId = 1, StartedAt = SourceNow.AddHours(-2), EndedAt = SourceNow.AddHours(-2).AddMinutes(10),
            DurationSeconds = 600, DetectionMethod = DetectionMethods.ProcessWatch,
        });
        if (kind == "activity")
            observations.Rows = [Row(1), Row(2) with { ComparisonUnavailable = true, UnexplainedMinutes = null }];
        else if (kind == "activity-scope")
            observations.Rows = [Row(1), Row(2, account: "other"), Row(3, 99), Row(4) with { UnexplainedMinutes = 0 }];
        else
        {
            using (var connection = database.Open())
            {
                connection.Execute("""
                    INSERT INTO works(id,name) VALUES(2,'Covered'),(3,'Hidden'),(4,'Other account');
                    INSERT INTO releases(id,work_id,name) VALUES(2,2,'Covered'),(3,3,'Hidden'),(4,4,'Other account');
                    INSERT INTO ownerships(id,release_id,store,account_ref,installed) VALUES(2,2,'steam','123',0),(3,3,'steam','123',0),(4,4,'steam','456',0);
                    INSERT INTO hidden_games(work_id,hidden_at) VALUES(3,'2026-09-14');
                    """);
            }
            // A separate real-repository population corroborates the API's visibility and
            // reconciliation boundary. Its recorded 600-second sitting precedes the window.
            using (var connection = database.Open())
                connection.Execute("UPDATE sessions SET started_at='2026-09-14 16:00:00',ended_at='2026-09-14 16:10:00' WHERE ownership_id=1");
            for (var id = 1; id <= 4; id++)
            {
                var account = id == 4 ? "456" : "123";
                await observations.ObserveAsync(new SteamPlaytimeObservation { OwnershipId = id, AccountRef = account,
                    Source = "steam_local", PlaytimeMinutes = 100, ObservedAt = Row(1).WindowStartedAt });
                await observations.ObserveAsync(new SteamPlaytimeObservation { OwnershipId = id, AccountRef = account,
                    Source = "steam_local", PlaytimeMinutes = 131, ObservedAt = Row(1).WindowEndedAt });
            }
            await new SessionRepository(database).InsertAsync(new Session
            {
                OwnershipId = 2, StartedAt = Row(1).WindowStartedAt, EndedAt = Row(1).WindowStartedAt.AddMinutes(31),
                DurationSeconds = 1860, DetectionMethod = DetectionMethods.ProcessWatch,
            });
        }
    }

    public async Task ChangeAsync(string stage)
    {
        var settings = new SettingsRepository(database);
        switch (stage)
        {
            case "all": await settings.SetAsync(AccountScope.SettingKey, AccountScope.All); break;
            case "own": await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own); break;
            case "fail": observations.Fail = true; await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "456"); break;
            case "missing-confirmation": observations.Fail = false; await settings.SetAsync(SteamOwnedAccount.RefSettingKey, ""); break;
            case "recover": observations.Fail = false; await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "000123"); break;
            default: throw new ArgumentException("Unknown reading fixture stage.");
        }
        await PublishAsync();
    }

    private async Task PublishAsync()
    {
        await publisher.PublishAsync(CancellationToken.None);
        changes.Publish("preferences.changed", "steam-account-reading-fixture");
    }

    public async Task<SteamReadingState> StateAsync()
    {
        var owners = await new OwnershipRepository(database).GetAllAsync();
        var facts = new AccountFactRepository(database);
        var sessions = new List<Session>();
        foreach (var owner in owners) sessions.AddRange(await new SessionRepository(database).GetByOwnershipAsync(owner.Id));
        var settings = new SettingsRepository(database);
        return new(_kind, SourceNow, owners.Count == 0 ? null : 1, _files,
            _kind == "unknown" ? OriginalPages(null) : null, _kind == "unknown" ? OriginalPages(10002) : null,
            owners, await new AccountAcquisitionRepository(database).GetAsync(owners.Select(owner => owner.Id).ToArray()),
            await facts.GetTransactionsAsync("steam"), await facts.GetLicensesAsync("steam"), sessions,
            sessions.Count, sessions.Sum(session => session.DurationSeconds ?? 0), Volatile.Read(ref _imports),
            observations.Calls.ToArray(), offline.Requests.ToArray(),
            await settings.GetAsync(AccountScope.SettingKey), await settings.GetAsync(SteamOwnedAccount.RefSettingKey));
    }

    public static SteamAccountPages OriginalPages(uint? account) => new()
    {
        SteamId = account is { } id ? SteamId.FromAccountId(id)!.Value.ToString() : null,
        Source = account is null ? SteamAccountPageSource.SavedFile : SteamAccountPageSource.EmbeddedSession,
        LicensesHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-account-pages", "licenses-page1.html")),
        HistoryHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-account-pages", "purchase-history.html")),
        CapturedAt = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
    };

    public static SteamReportedActivity Row(long id, long ownership = 1, string account = "123") => new()
    {
        Id = id, OwnershipId = ownership, AccountRef = account,
        WindowStartedAt = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc),
        WindowEndedAt = new(2026, 9, 14, 19, 0, 0, DateTimeKind.Utc),
        SteamDeltaMinutes = 31, UnexplainedMinutes = 31, MatchedRecordedMinutes = 0,
    };

    private async Task<SteamReadingFile> WriteAsync(string name, string content)
    {
        var path = Path.Combine(_directory, "docs", name);
        await File.WriteAllTextAsync(path, content);
        return new(name, path, content);
    }

    private static string Page(string title, int page) => $"""
        <div class="license_paginator_ctn"><span>Showing licenses {page}-{page} of 2</span></div>
        <table class="account_table"><tr><th class="license_date_col">Date</th><th>Item</th></tr>
        <tr><td class="license_date_col">Sep 1, 2026</td><td>{title}</td><td class="license_acquisition_col">Steam Store</td></tr></table>
        """;
}

internal sealed record SteamReadingSeed(string Kind);
internal sealed class SteamReadingCaptureClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
}
internal sealed record SteamReadingChange(string Stage);
internal sealed record SteamReadingFile(string Name, string Path, string Content);
internal sealed record SteamReadingCall(IReadOnlyList<long> OwnershipIds, string? AccountRef, bool Failed);
internal sealed record SteamReadingState(string? Kind, DateTime Now, long? WorkId, IReadOnlyList<SteamReadingFile> Files,
    SteamAccountPages? SourcePages, SteamAccountPages? NamedPages, IReadOnlyList<Ownership> Ownerships,
    IReadOnlyList<OwnershipAcquisitionObservation> Acquisitions, IReadOnlyList<AccountTransactionFact> Transactions,
    IReadOnlyList<AccountLicenseFact> Licenses, IReadOnlyList<Session> Sessions, int SessionCount, long SessionSeconds,
    int ImportCount, IReadOnlyList<SteamReadingCall> ActivityCalls, IReadOnlyList<string> ProviderRequests,
    string? AccountScope, string? ConfirmedAccount);

internal sealed class SteamReadingObservations(ISqliteConnectionFactory database) : ISteamPlaytimeObservationRepository
{
    public IReadOnlyList<SteamReportedActivity>? Rows { get; set; }
    public bool Fail { get; set; }
    public ConcurrentQueue<SteamReadingCall> Calls { get; } = new();
    public Task ObserveAsync(SteamPlaytimeObservation observation, CancellationToken ct = default)
        => new SteamPlaytimeObservationRepository(database).ObserveAsync(observation, ct);
    public Task<IReadOnlyList<SteamPlaytimeObservation>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
        => new SteamPlaytimeObservationRepository(database).GetByOwnershipAsync(ownershipId, ct);
    public Task<IReadOnlyList<SteamReportedActivity>> GetActivityAsync(IReadOnlyCollection<long> ownershipIds, DateTime asOfUtc,
        string? accountRef = null, CancellationToken ct = default)
    {
        Calls.Enqueue(new(ownershipIds.ToArray(), accountRef, Fail));
        if (Fail) throw new IOException("Source activity repository failure.");
        return Rows is { } rows ? Task.FromResult(rows)
            : new SteamPlaytimeObservationRepository(database).GetActivityAsync(ownershipIds, asOfUtc, accountRef, ct);
    }
}

internal sealed class SteamReadingOfflineHttp : IHttpMessageHandlerBuilderFilter
{
    public ConcurrentQueue<string> Requests { get; } = new();
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.PrimaryHandler = new Handler(Requests);
    };
    private sealed class Handler(ConcurrentQueue<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }
}
