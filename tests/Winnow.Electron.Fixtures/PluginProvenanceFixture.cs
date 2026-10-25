using System.Collections.Concurrent;
using System.Runtime.Loader;
using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Winnow.App.Services;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginFixture;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.Electron.Fixtures;

/// <summary>Exact presentation fixtures through real repositories, action observations and plugin settings.</summary>
internal sealed class PluginProvenanceFixture(ISqliteConnectionFactory database, IMetadataCache cache,
    LibraryChangePublisher publisher, PluginStorage storage, PluginCatalog catalog, PluginSettingsBackend settings,
    ProvenanceOfflineHttp offline)
{
    public const string SourceLabel = "Played history — not proof of ownership.";
    private readonly ConcurrentQueue<string> _requests = new();
    private readonly ConcurrentQueue<Dictionary<string, string>> _writes = new();
    private readonly ConcurrentQueue<string> _removed = new();
    private string? _kind;
    private int _refreshRequests;
    public DateTime Now { get; } = DateTime.UtcNow;

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<PluginProvenanceFixture>();
        services.AddSingleton<ProvenanceOfflineHttp>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(provider => provider.GetRequiredService<ProvenanceOfflineHttp>());
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, ProvenanceNoCredentials>();
    }

    public async Task InitializeAsync(string directory)
    {
        var package = Path.Combine(directory, "plugins", "psn");
        Directory.CreateDirectory(package);
        File.Copy(typeof(PresentationFixturePlugin).Assembly.Location, Path.Combine(package, "Winnow.PluginFixture.dll"));
        await File.WriteAllTextAsync(Path.Combine(package, "plugin.json"), JsonSerializer.Serialize(new PluginManifest
        {
            Id = "psn", Name = "PlayStation", Version = "1.0.0",
            Description = "Import PlayStation games and optional history.",
            EntryAssembly = "Winnow.PluginFixture.dll", EntryType = typeof(PresentationFixturePlugin).FullName!,
            Capabilities = [PluginCapabilities.Library, PluginCapabilities.Metadata, PluginCapabilities.Artwork],
            Settings = [
                new() { Key = "npsso", Label = "Sony session token (NPSSO)", Secret = true,
                    SetupUrl = "https://ca.account.sony.com/api/v1/ssocookie" },
                new() { Key = "import-history", Label = "Include played games", IsBoolean = true },
                new() { Key = "include-legacy", Label = "Include PS3 and PS Vita trophy history", IsBoolean = true },
            ],
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await storage.SetEnabledAsync("psn", true);
        await storage.WriteSecretAsync("psn", "npsso", "stored-fixture-token");
        await storage.WriteSettingAsync("psn", "import-history", "false");
        await storage.WriteSettingAsync("psn", "include-legacy", "false");
        settings.RefreshRequested += () => Interlocked.Increment(ref _refreshRequests);
    }

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<PluginProvenanceFixture>();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1"))
                fixture._requests.Enqueue(context.Request.Method + " " + context.Request.Path);
            if (context.Request.Method == "PUT" && context.Request.Path == "/api/v1/connections/plugins/psn/settings")
            {
                context.Request.EnableBuffering();
                using var document = await JsonDocument.ParseAsync(context.Request.Body);
                fixture._writes.Enqueue(document.RootElement.GetProperty("values").EnumerateObject()
                    .ToDictionary(item => item.Name, item => item.Value.GetString()!));
                context.Request.Body.Position = 0;
            }
            if (context.Request.Method == "DELETE" && context.Request.Path == "/api/v1/connections/plugins/psn/secrets/npsso")
                fixture._removed.Enqueue("npsso");
            await next(context);
        });
        app.MapPost("/__fixture/plugin-provenance/seed", (ProvenanceSeed input) => fixture.SeedAsync(input.Kind));
        app.MapGet("/__fixture/plugin-provenance/state", () => fixture.StateAsync());
    }

    public async Task<ProvenanceState> SeedAsync(string kind)
    {
        if (_kind is not null) throw new InvalidOperationException("Seed once per isolated source fixture.");
        if (kind is not ("grouped" or "psn" or "filter" or "settings")) throw new ArgumentException("Unknown fixture kind.");
        await catalog.DiscoveryReady;
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0) throw new InvalidOperationException("Empty library required.");
            using var transaction = connection.BeginTransaction();
            connection.Execute("""
                INSERT OR REPLACE INTO settings(key,value) VALUES('appearance.theme','winnow');
                INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true');
                INSERT OR REPLACE INTO settings(key,value) VALUES('application.link_destination','browser');
                """, transaction: transaction);
            if (kind == "filter")
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Game 1','Game 1'),(2,'Game 2','Game 2');
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'plugin:psn',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','1'),(2,'steam','2');
                    INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                    INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
                    """, transaction: transaction);
            else if (kind != "settings")
            {
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Fixture','Fixture');
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Fixture','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,@store,0);
                    """, new { store = kind == "grouped" ? "steam" : "plugin:psn" }, transaction);
                if (kind == "grouped")
                    connection.Execute("""
                        INSERT INTO releases(id,work_id,name,platform) VALUES(2,1,'Fixture','windows');
                        INSERT INTO ownerships(id,release_id,store,installed) VALUES(2,2,'plugin:xbox',0);
                        """, transaction: transaction);
                else connection.Execute("""
                    INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at)
                    VALUES(1,60,@last,'plugin:psn',@now);
                    """, new { last = Now.AddDays(-1), now = Now }, transaction);
            }
            transaction.Commit();
        }
        // The original UI directly injected this observation. The API adapter persists the same
        // immutable facts so PluginGameActionService, not a rewritten response, produces the DTO.
        if (kind is "grouped" or "psn")
            await cache.SetAsync("plugin-library-actions", kind == "grouped" ? "xbox:2" : "psn:1",
                JsonSerializer.Serialize(new { SourceLabel, Actions = Array.Empty<int>() }), Now);
        _kind = kind;
        await publisher.PublishAsync(default);
        return await StateAsync();
    }

    public async Task<ProvenanceState> StateAsync()
    {
        using var connection = database.Open();
        var loaded = catalog.GetActive<ILibrarySourcePlugin>().SingleOrDefault(plugin => plugin.Manifest.Id == "psn");
        var isolated = loaded is null ? null : await catalog.InvokeAsync(loaded, (instance, _) => Task.FromResult<string?>(
            AssemblyLoadContext.GetLoadContext(instance.GetType().Assembly) != AssemblyLoadContext.Default ? "isolated" : "default"));
        return new(_kind, Now, connection.Query<ProvenanceOwnership>("SELECT id,release_id AS ReleaseId,store FROM ownerships ORDER BY id").ToArray(),
            _requests.ToArray(), _writes.ToArray(), _removed.ToArray(), Volatile.Read(ref _refreshRequests),
            await storage.HasStoredSecretAsync("psn", "npsso"), await storage.ReadSecretAsync("psn", "npsso"),
            await storage.ReadSettingAsync("psn", "import-history"), await storage.ReadSettingAsync("psn", "include-legacy"),
            loaded is not null, isolated == "isolated", offline.Requests.ToArray());
    }
}

internal sealed record ProvenanceSeed(string Kind);
internal sealed record ProvenanceOwnership(long Id, long ReleaseId, string Store);
internal sealed record ProvenanceState(string? Kind, DateTime Now, ProvenanceOwnership[] Ownerships,
    string[] Requests, Dictionary<string, string>[] Writes, string[] RemovedSecrets, int RefreshRequests,
    bool StoredSecret, string? Secret, string? ImportHistory, string? IncludeLegacy, bool Loaded, bool Isolated, string[] ProviderRequests);
internal sealed class ProvenanceNoCredentials : IIgdbCredentialProvider
{
    public void Invalidate() { }
    public ValueTask<IgdbCredentials?> GetAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IgdbCredentials?>(null);
}

internal sealed class ProvenanceOfflineHttp : IHttpMessageHandlerBuilderFilter
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
