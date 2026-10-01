using System.Collections.Concurrent;
using System.Net;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Stores;

namespace Winnow.Electron.Fixtures;

/// <summary>Original ownership/storefront inputs, with real SQLite and application projections.</summary>
internal sealed class OwnershipSnapshotFixture(ISqliteConnectionFactory database,
    LibraryChangePublisher publisher, OwnershipStorefrontHttp remote)
{
    internal const string Epic = """{"fn":"fortnite","min":"hades","bad":"../../login","wrong":12}""";
    internal const string Gog = """{"id":1207658871,"links":{"product_card":"https://www.gog.com/game/panzer_general_2"},"changelog":"<h4>Internal Update</h4><ul><li>Cloud Saves support</li></ul><script>bad()</script>"}""";
    private readonly ConcurrentQueue<string> _requests = new();
    private string? _kind;

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<OwnershipSnapshotFixture>();
        services.AddSingleton<OwnershipStorefrontHttp>();
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, OwnershipNoCredentials>();
    }

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<OwnershipSnapshotFixture>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1"))
                fixture._requests.Enqueue(context.Request.Method + " " + context.Request.Path);
            await next(context);
        });
        app.MapPost("/__fixture/ownership-snapshot/seed", (OwnershipSnapshotSeed input) => fixture.SeedAsync(input));
        app.MapPost("/__fixture/ownership-snapshot/confirm", async () =>
        {
            await fixture.ConfirmAsync();
            return Results.NoContent();
        });
        app.MapPost("/__fixture/ownership-snapshot/sync", async () =>
        {
            await fixture.SyncAsync();
            return Results.NoContent();
        });
        app.MapPost("/__fixture/ownership-snapshot/epic-link", async () =>
        {
            await fixture.AddEpicLinkAsync();
            return Results.NoContent();
        });
        app.MapGet("/__fixture/ownership-snapshot/state", () => fixture.SnapshotAsync());
    }

    public async Task<OwnershipSnapshotState> SeedAsync(OwnershipSnapshotSeed input)
    {
        if (_kind is not null) throw new InvalidOperationException("Seed an ownership fixture only once.");
        if (input.Kind is not ("account" or "account-empty" or "storefront" or "owned-sync"))
            throw new ArgumentException("Unknown ownership fixture kind.");
        if (input.OtherAccounts is < 1 or > 1234) throw new ArgumentOutOfRangeException(nameof(input));
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Ownership fixtures require an empty library.");
            using var transaction = connection.BeginTransaction();
            connection.Execute("""
                INSERT OR REPLACE INTO settings(key,value) VALUES('appearance.theme','winnow');
                INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true');
                """, transaction: transaction);
            if (input.Kind == "account")
            {
                connection.Execute("""
                    WITH RECURSIVE ids(id) AS (SELECT 1 UNION ALL SELECT id+1 FROM ids WHERE id<@total)
                    INSERT INTO works(id,name,sort_name)
                    SELECT id,CASE WHEN id=1 THEN 'My game' ELSE 'Other account game '||CAST(id-1 AS TEXT) END,
                        CASE WHEN id=1 THEN 'My game' ELSE 'Other account game '||CAST(id-1 AS TEXT) END FROM ids;
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO ownerships(id,release_id,store,account_ref,installed)
                        SELECT id,id,'steam',CASE WHEN id=1 THEN '11111' ELSE '22222' END,0 FROM releases;
                    INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
                    INSERT INTO ownership_accounts(ownership_id,account_ref,playtime_minutes,source,first_seen_at,last_seen_at)
                        SELECT id,account_ref,0,'steam_local','2026-08-01','2026-09-01' FROM ownerships;
                    INSERT INTO play_records(ownership_id,playtime_minutes,source,observed_at)
                        SELECT id,0,'steam_local','2026-09-01' FROM ownerships;
                    """, new { total = input.OtherAccounts + 1 }, transaction);
            }
            else if (input.Kind is "storefront" or "owned-sync")
            {
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,@gog,@gog),(2,@epic,@epic);
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'gog','1207658871'),(2,'epic','catalog-id');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'gog',0),(2,2,'epic',0);
                    """, new { gog = input.Kind == "storefront" ? "Panzer General 2" : "Owned GOG", epic = input.Kind == "storefront" ? "Epic game" : "Owned Epic" }, transaction);
                if (input.Kind == "owned-sync")
                    connection.Execute("""
                        INSERT INTO works(id,name,sort_name) VALUES(3,'Unowned GOG','Unowned GOG');
                        INSERT INTO releases(id,work_id,name) VALUES(3,3,'Unowned GOG');
                        INSERT INTO external_ids(release_id,provider,provider_id) VALUES(3,'gog','99');
                        """, transaction: transaction);
                else connection.Execute("""
                    INSERT INTO play_records(ownership_id,playtime_minutes,source,observed_at) VALUES(1,0,'gog','2026-09-01');
                    """, transaction: transaction);
            }
            transaction.Commit();
        }
        if (input.Kind == "account")
        {
            var inventories = new OwnershipInventoryRepository(database);
            var attempt = await inventories.BeginAttemptAsync("steam", "11111", OwnershipInventorySources.SteamOwnedGames);
            if (!await inventories.CompleteAsync(attempt, new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 1))
                throw new InvalidOperationException("Fixture inventory completion failed.");
        }
        if (input.Kind is "storefront" or "owned-sync")
        {
            await new SqliteEpicLaunchKeyStore(database).SaveAsync([new("catalog-id", input.Kind == "owned-sync" ? "fn" : "min", input.Kind == "owned-sync" ? "Fortnite" : "Hades")]);
            if (input.Kind == "storefront")
                await new StorefrontCache(database).SaveAsync("gog:1207658871", Gog, DateTime.UtcNow);
        }
        _kind = input.Kind;
        await publisher.PublishAsync(CancellationToken.None);
        return await SnapshotAsync();
    }

    public async Task ConfirmAsync()
    {
        if (_kind is not ("account" or "account-empty")) throw new InvalidOperationException("Not an account fixture.");
        await new SettingsRepository(database).SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await publisher.PublishAsync(CancellationToken.None);
    }

    public async Task SyncAsync()
    {
        if (_kind != "owned-sync") throw new InvalidOperationException("Not an owned-sync fixture.");
        using var http = new HttpClient(remote, disposeHandler: false);
        await new StorefrontSyncService(database, new(http, new(database), TimeProvider.System),
            NullLogger<StorefrontSyncService>.Instance, new SqliteEpicLaunchKeys(database)).SyncAsync();
        await publisher.PublishAsync(CancellationToken.None);
    }

    public async Task AddEpicLinkAsync()
    {
        if (_kind != "storefront") throw new InvalidOperationException("Not a storefront fixture.");
        await new StorefrontCache(database).SaveAsync("epic", Epic, DateTime.UtcNow);
        await publisher.PublishAsync(CancellationToken.None);
    }

    public async Task<OwnershipSnapshotState> SnapshotAsync()
    {
        using var connection = database.Open();
        return new(_kind,
            connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key=@key", new { key = SteamOwnedAccount.RefSettingKey }),
            connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key=@key", new { key = AccountScope.SettingKey }),
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works"),
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ownerships"),
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM account_inventory_observations WHERE is_complete=1"),
            _requests.ToArray(), remote.Urls.ToArray(), await new StorefrontCache(database).ReadAllAsync());
    }
}

internal sealed record OwnershipSnapshotSeed(string Kind, int OtherAccounts = 1);
internal sealed record OwnershipSnapshotState(string? Kind, string? ConfirmedAccount, string? AccountScope,
    int WorkCount, int OwnershipCount, int CompleteInventories, IReadOnlyList<string> Requests,
    IReadOnlyList<string> ProviderRequests, IReadOnlyDictionary<string, Winnow.Core.Repositories.StorefrontDetails> Storefronts);

internal sealed class OwnershipStorefrontHttp : HttpMessageHandler
{
    public ConcurrentQueue<string> Urls { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        Urls.Enqueue(url);
        var payload = url == StorefrontClient.EpicMappingUrl ? OwnershipSnapshotFixture.Epic
            : url == "https://api.gog.com/products/1207658871?expand=changelog" ? OwnershipSnapshotFixture.Gog
            : throw new InvalidOperationException("Unexpected provider request in ownership fixture.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}

internal sealed class OwnershipNoCredentials : IIgdbCredentialProvider
{
    public ValueTask<IgdbCredentials?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult<IgdbCredentials?>(null);
    public void Invalidate() { }
}
