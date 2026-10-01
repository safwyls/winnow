using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.App.Services;
using Winnow.Application;
using Winnow.Core.Auth;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;

namespace Winnow.Electron.Fixtures;

internal sealed class PlatformContextFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
    IApplicationChangePublisher changes, PlatformKeys keys, ISteamAccountConfirmation confirmation,
    PlatformSteamSession steam, PlatformEpicSession epic)
{
    private string? _kind;
    private string _store = "epic";
    private OwnershipInventoryAttempt? _attempt;
    private readonly ConcurrentQueue<string> _requests = new();

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<PlatformContextFixture>();
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, OwnershipNoCredentials>();
        services.AddSingleton<PlatformKeys>();
        services.AddSingleton<ISteamApiKeyProvider>(sp => sp.GetRequiredService<PlatformKeys>());
        services.AddSingleton<ISteamApiKeyStore>(sp => sp.GetRequiredService<PlatformKeys>());
        services.AddSingleton<PlatformSteamSession>();
        services.AddSingleton<ISteamSessionProvider>(sp => sp.GetRequiredService<PlatformSteamSession>());
        services.AddSingleton<ISteamSessionCredentialSource, SteamSessionCredentialSource>();
        services.AddSingleton<ISteamCredentialProvider, SteamCredentialProvider>();
        services.AddSingleton<ISteamAccountConfirmation, SteamAccountConfirmation>();
        services.AddSingleton<PlatformEpicSession>();
        services.AddSingleton<IEpicTokenProvider>(sp => sp.GetRequiredService<PlatformEpicSession>());
        services.AddSingleton<IEpicTokenStore>(sp => sp.GetRequiredService<PlatformEpicSession>());
        services.AddSingleton<IEpicCredentialProvider, PlatformEpicCredentials>();
    }

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<PlatformContextFixture>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1"))
                fixture._requests.Enqueue(context.Request.Method + " " + context.Request.Path);
            await next(context);
        });
        app.MapPost("/__fixture/platform-context/seed", (PlatformSeed input) => fixture.SeedAsync(input));
        app.MapPost("/__fixture/platform-context/change", async (PlatformChange input) =>
        {
            await fixture.ChangeAsync(input.Stage);
            return Results.NoContent();
        });
        app.MapGet("/__fixture/platform-context/state", () => fixture.StateAsync());
    }

    public async Task<object> SeedAsync(PlatformSeed input)
    {
        if (_kind is not null) throw new InvalidOperationException("Seed the platform fixture only once.");
        if (input.Kind is not ("inventory" or "key" or "install" or "acquisition" or "platforms"))
            throw new ArgumentException("Unknown platform fixture kind.");
        if (input.Store is not ("epic" or "gog")) throw new ArgumentException("Unknown installation store.");
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Platform fixtures require an empty library.");
            using var transaction = connection.BeginTransaction();
            if (input.Kind == "inventory")
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'My game','My game'),(2,'Household game','Household game');
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'steam',0);
                    INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
                        VALUES(1,'111','steam_local','2026-08-01','2026-08-01'),(2,'222','steam_local','2026-08-01','2026-08-01');
                    INSERT INTO settings(key,value) VALUES('library.account_scope','own'),('steam.owned_account_ref','111');
                    """, transaction: transaction);
            else
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Game 1','Game 1'),(2,'Game 2','Game 2');
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                    INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
                    INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                    INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
                    """, transaction: transaction);
            if (input.Kind == "install")
                connection.Execute("""
                    UPDATE ownerships SET store=@store,installed=1,install_path='C:\Fixture\Game' WHERE id=1;
                    UPDATE external_ids SET provider=@store,provider_id=@providerId WHERE release_id=1;
                    """, new { store = input.Store, providerId = input.Store == "gog" ? "12345" : "sample-item" }, transaction);
            transaction.Commit();
        }
        _kind = input.Kind; _store = input.Store;
        if (input.Kind == "inventory")
        {
            _attempt = await new OwnershipInventoryRepository(database).BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames);
            if (input.Complete) await CompleteAsync();
        }
        if (input.Kind == "key")
        {
            await keys.SaveAsync("original-test-key");
            if (!await confirmation.ConfirmAsync(SteamId.FromAccountId(10001)!.Value, SteamAccountConfirmationSource.WebApiKey))
                throw new InvalidOperationException("Original key confirmation failed.");
        }
        if (input.Kind == "install" && input.Store == "epic")
            await new SqliteEpicLaunchKeyStore(database).SaveAsync([new("sample-item", "sample-namespace", "FixtureGame")]);
        if (input.Kind == "acquisition")
        {
            var acquisitions = new AccountAcquisitionRepository(database);
            var first = new OwnershipAcquisitionObservation { OwnershipId = 1, AccountRef = "10001",
                AcquiredAt = new(2020, 1, 2, 12, 0, 0, DateTimeKind.Utc), LicenseType = "retail", Source = "steam",
                CapturedAt = DateTime.UtcNow, PricePaidCents = 0, PriceSource = PriceSources.SteamAccountHistory };
            await acquisitions.TryAppendAsync(first);
            await acquisitions.TryAppendAsync(first with { AccountRef = "10002", AcquiredAt = new(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc), LicenseType = "gift" });
            var settings = new SettingsRepository(database);
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
            await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10002");
            var facts = new AccountFactRepository(database);
            var fact = new AccountTransactionFact { Source = "steam", AccountRef = "10001", Kind = "purchase",
                TransactionTypeRaw = "Purchase", ItemNames = ["Fixture game"], OccurredAt = DateTime.UtcNow,
                TotalCents = 500, CurrencySymbol = "$", CapturedAt = DateTime.UtcNow };
            await facts.TryAppendAsync(fact);
            await facts.TryAppendAsync(fact with { AccountRef = "10002" });
            await facts.TryAppendAsync(fact with { AccountRef = null });
        }
        if (input.Kind == "platforms") epic.Set("Account A", true);
        await PublishAsync();
        return await StateAsync();
    }

    private async Task CompleteAsync()
    {
        if (_attempt is null || !await new OwnershipInventoryRepository(database).CompleteAsync(_attempt, DateTime.UtcNow, 1))
            throw new InvalidOperationException("Inventory completion failed.");
    }

    public async Task ChangeAsync(string stage)
    {
        switch (stage)
        {
            case "complete" when _kind == "inventory": await CompleteAsync(); break;
            case "failed" when _kind == "inventory":
                _attempt = await new OwnershipInventoryRepository(database).BeginAttemptAsync("steam", "111", OwnershipInventorySources.SteamOwnedGames); break;
            case "unknown" when _kind == "install":
                await new OwnershipRepository(database).UpsertAsync(new(1, _store, null, null, null, null)); break;
            case "absent" when _kind == "install":
                await new OwnershipRepository(database).UpsertAsync(new(1, _store, null, null, null, false)); break;
            case "all" when _kind == "acquisition":
                await new SettingsRepository(database).SetAsync(AccountScope.SettingKey, AccountScope.All); break;
            case "epic-expired": epic.Set("Test player", false); break;
            case "epic-a": epic.Set("Account A", true); break;
            case "epic-live": epic.Set("Test player", true); break;
            case "epic-next-new": epic.NextName = "New test player"; break;
            case "epic-next-b": epic.NextName = "Account B"; break;
            case "steam-live": steam.Set(true); break;
            case "steam-expired": steam.Set(false); break;
            case "steam-none": await steam.SignOutAsync(); break;
            default: throw new ArgumentException("Unknown platform fixture change.");
        }
        await PublishAsync();
    }

    private async Task PublishAsync()
    {
        changes.Publish("connections.changed", "fixture");
        await publisher.PublishAsync(CancellationToken.None);
    }

    public async Task<object> StateAsync()
    {
        using var connection = database.Open();
        return new { Kind = _kind, Requests = _requests.ToArray(),
            ConfirmedAccount = connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key='steam.owned_account_ref'"),
            AccountScope = connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key='library.account_scope'"),
            KeyPresent = await keys.GetAsync() is not null,
            Inventories = connection.Query("SELECT store,account_ref,revision,is_complete,item_count FROM account_inventory_observations ORDER BY store,account_ref"),
            Ownerships = await new OwnershipRepository(database).GetAllAsync(),
            Epic = new { Name = epic.Current?.DisplayName, Live = epic.Current?.IsRefreshUsable(DateTimeOffset.UtcNow, TimeSpan.Zero) == true },
            EpicExchanges = epic.Exchanges, KeyWrites = keys.Writes };
    }
}

internal sealed record PlatformSeed(string Kind, string Store = "epic", bool Complete = false);
internal sealed record PlatformChange(string Stage);

// These are the original source's controlled credential/session inputs. Actual credential selection,
// confirmation, StoreConnectionApplication, SQL projection, and authenticated HTTP routes remain real.
internal sealed class PlatformKeys : ISteamApiKeyProvider, ISteamApiKeyStore
{
    private string? _key;
    public int Writes { get; private set; }
    public ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult(SteamApiKey.TryCreate(_key, SettingsTableApiKeySource.SourceName));
    ValueTask<string?> ISteamApiKeyStore.GetAsync(CancellationToken ct) => ValueTask.FromResult(_key);
    public void Invalidate() { }
    public Task<SteamApiKeySaveOutcome> SaveAsync(string key, CancellationToken ct = default)
    { _key = string.IsNullOrWhiteSpace(key) ? null : key; Writes++; return Task.FromResult(SteamApiKeySaveOutcome.Stored); }
    public Task ClearAsync(CancellationToken ct = default) { _key = null; Writes++; return Task.CompletedTask; }
}

internal sealed class PlatformSteamSession : ISteamSessionProvider
{
    private SteamSession? _session;
    public void Set(bool live) => _session = new("fixture-access-token", DateTimeOffset.UtcNow.AddHours(live ? 1 : -1),
        ["web:community"], "steam", SteamId.FromSteamId64(76561198000000000UL)!.Value, null, null, DateTimeOffset.UtcNow);
    public ValueTask<SteamSession?> GetAsync(CancellationToken ct = default) => ValueTask.FromResult(_session);
    public ValueTask<SteamSessionHealth> GetHealthAsync(CancellationToken ct = default) => ValueTask.FromResult(_session is null
        ? SteamSessionHealth.NotSignedIn : _session.ExpiresAt > DateTimeOffset.UtcNow ? SteamSessionHealth.Live : SteamSessionHealth.Expired);
    public Task SaveAsync(SteamSession session, CancellationToken ct = default) { _session = session; return Task.CompletedTask; }
    public Task SignOutAsync(CancellationToken ct = default) { _session = null; return Task.CompletedTask; }
    public bool IsRenewalDue(SteamSession session) => false;
    public Task<SteamSession?> RenewAsync(SteamSession? staleSession, CancellationToken ct = default) => Task.FromResult(_session);
}

internal sealed class PlatformEpicSession : IEpicTokenProvider, IEpicTokenStore
{
    public EpicOAuthToken? Current { get; private set; }
    public string NextName { get; set; } = "Account B";
    public int Exchanges { get; private set; }
    public bool CanPersist => true;
    public void Set(string name, bool live) => Current = new("fixture-client", "fixture-access", "fixture-refresh", "fixture-account", name,
        DateTimeOffset.UtcNow.AddHours(live ? 1 : -1), DateTimeOffset.UtcNow.AddDays(live ? 1 : -1));
    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
    public ValueTask<bool> IsSignedInAsync(CancellationToken ct = default) => ValueTask.FromResult(Current?.IsRefreshUsable(DateTimeOffset.UtcNow, TimeSpan.Zero) == true);
    public Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(string authorizationCode, CancellationToken ct = default)
    { Exchanges++; Set(NextName, true); return Task.FromResult(new EpicSignInResult(true, EpicSignInFailure.None, Current!.AccountId, Current.DisplayName, true)); }
    public Task<EpicSignInResult> SignInWithExchangeCodeAsync(string exchangeCode, CancellationToken ct = default) => SignInWithAuthorizationCodeAsync(exchangeCode, ct);
    public Task<EpicOAuthToken?> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public ValueTask<EpicSessionIdentity?> GetIdentityAsync(CancellationToken ct = default) => ValueTask.FromResult(Current is null ? null : new EpicSessionIdentity(Current.AccountId, Current.ClientId, Exchanges));
    public Task<EpicOAuthToken?> RefreshAsync(EpicOAuthToken? staleToken, CancellationToken ct = default) => Task.FromResult(Current);
    public Task SignOutAsync(CancellationToken ct = default) { Current = null; return Task.CompletedTask; }
    public Task<EpicOAuthToken?> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task SaveAsync(EpicOAuthToken token, CancellationToken ct = default) { Current = token; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken ct = default) => SignOutAsync(ct);
}

internal sealed class PlatformEpicCredentials : IEpicCredentialProvider
{
    public ValueTask<EpicClientCredentials?> GetAsync(CancellationToken ct = default)
        => ValueTask.FromResult(EpicClientCredentials.TryCreate("fixture-client", "fixture-secret", "test"));
    public void Invalidate() { }
}
