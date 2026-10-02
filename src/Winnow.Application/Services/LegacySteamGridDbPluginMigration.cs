using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;

namespace Winnow.App.Services;

/// <summary>Preserves installations that configured SteamGridDB before it became a plugin.</summary>
public sealed class LegacySteamGridDbPluginMigration(ISqliteConnectionFactory factory, PluginStorage storage,
    ISettingsStore settings, IConfiguration configuration, IMetadataCache cache, IWorkImageRepository images, CoverDiskCache disk)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configuration["Plugins:steamgriddb:apikey"]))
            configuration["Plugins:steamgriddb:apikey"] = configuration["SteamGridDb:ApiKey"]
                ?? Environment.GetEnvironmentVariable("SteamGridDb__ApiKey");
        const string legacySecret = "steamgriddb.api_key.protected";
        var encrypted = await settings.GetAsync(legacySecret, ct);
        if (encrypted is not null)
        {
            if (await storage.ReadStoredSecretAsync("steamgriddb", "apikey", ct) is null)
            {
                var secret = PluginStorage.Unprotect(encrypted, Encoding.UTF8.GetBytes("Winnow.SteamGridDb.ApiKey.v1"));
                if (secret is not null) await storage.WriteSecretAsync("steamgriddb", "apikey", secret, ct);
            }
            if (await storage.ReadStoredSecretAsync("steamgriddb", "apikey", ct) is not null) await settings.RemoveAsync(legacySecret, ct);
        }
        if (await settings.GetAsync("plugins.steamgriddb.migrated.v1", ct) == "true") return;
        using var lease = factory.Lease();
        var cached = await lease.Connection.QueryAsync<LegacyCacheRow>(new CommandDefinition(
            "SELECT provider_id AS Id,payload_json AS Json,fetched_at AS FetchedAt FROM metadata_cache WHERE provider='steamgriddb';",
            transaction: lease.Transaction, cancellationToken: ct));
        foreach (var entry in cached)
            if (entry.Json is { } json && await storage.ReadCacheAsync("steamgriddb", entry.Id, ct) is null)
                await storage.WriteCacheAsync("steamgriddb", entry.Id,
                    new(Encoding.UTF8.GetBytes(json), new DateTimeOffset(DateTime.SpecifyKind(entry.FetchedAt, DateTimeKind.Utc)).AddDays(30)), ct);
        var workIds = await lease.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT DISTINCT work_id FROM work_images WHERE source='steamgriddb';", transaction: lease.Transaction, cancellationToken: ct));
        foreach (var workId in workIds)
        {
            var rows = await images.GetForWorkAsync(workId, ct);
            foreach (var row in rows.Where(r => r.Source == ImageSources.SteamGridDb))
            {
                foreach (var image in row.Images)
                {
                    if (SteamGridDbHeroUrl.Key(image.Url) is not { } oldKey || PluginArtRef.Key("steamgriddb", image.Url) is not { } newKey) continue;
                    await PluginArtworkSource.RegisterAsync(cache, "steamgriddb", image.Url!, ct);
                    if (File.Exists(disk.SourcePath(oldKey)) && !File.Exists(disk.SourcePath(newKey)))
                        File.Copy(disk.SourcePath(oldKey), disk.SourcePath(newKey), overwrite: false);
                }
                if (!rows.Any(r => r.Source == "plugin:steamgriddb" && r.Kind == row.Kind))
                    await images.UpsertAsync(row with { Source = "plugin:steamgriddb" }, ct);
                await images.DeleteAsync(workId, row.Source, row.Kind, ct);
            }
        }
        await settings.SetAsync("plugins.steamgriddb.migrated.v1", "true", ct);
    }

    private sealed record LegacyCacheRow(string Id, string? Json, DateTime FetchedAt);
}
