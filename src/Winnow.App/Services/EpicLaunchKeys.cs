using System.Text.Json;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Data;

namespace Winnow.App.Services;

/// <summary>
/// Epic's <c>namespace : catalogItemId : artifactId</c> for every catalog item
/// this app has an answer cached for.
///
/// <para><b>Why this is needed at all.</b> <c>external_ids</c> stores one id per
/// release, and for Epic that is the catalog item id — the stable identity, and
/// correctly the thing a release is keyed by. But Epic's launcher addresses a
/// title by all three parts of its composite key, so the id that identifies the
/// game is not the id that launches it. The other two are already in the
/// database: <see cref="SqliteEpicCatalogCache"/> writes the catalog answer for
/// each item into <c>metadata_cache</c>, and that payload carries the namespace
/// and the artifact codename.</para>
///
/// <para><b>Why the UI may read it.</b> This is the app reading a table the app
/// itself writes — <see cref="SqliteEpicCatalogCache"/> lives in this same
/// composition root, for the reason its own remarks give — not the UI calling
/// into <c>Winnow.Ingest.Epic</c>. CLAUDE.md's rule is that the UI reads the
/// database and raises commands, and that is exactly this: one query, no
/// network, no ingest type crossing the seam. The payload is read field by field
/// with <see cref="JsonDocument"/> rather than deserialized into the Epic
/// module's record, so the two are coupled by two field names instead of by a
/// type.</para>
///
/// <para><b>Coverage is partial and that is a normal state.</b> The catalog
/// backfill runs behind a library the user is already browsing (§7), so a title
/// it has not reached yet has no launch key, which renders as no Play button
/// rather than as a broken one — the same answer §10.3 gives a Steam release
/// with no appid. A cached MISS (null payload) is a real answer meaning Epic
/// does not recognise the item, and produces no key either.</para>
///
/// <para>Where this should end up: the launch key belongs in the database beside
/// the id, written by the Epic ingest, rather than recovered from an enrichment
/// cache. That is a migration and an ingest change, so it is recorded here
/// rather than done here.</para>
/// </summary>
public interface IEpicLaunchKeys
{
    /// <summary>Catalog item id → launch key, for every item with a usable one.</summary>
    Task<IReadOnlyDictionary<string, EpicLaunchKey>> GetAllAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IEpicLaunchKeys"/>
public sealed class SqliteEpicLaunchKeys : IEpicLaunchKeys
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteEpicLaunchKeys(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyDictionary<string, EpicLaunchKey>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT provider_id AS CatalogItemId, payload_json AS PayloadJson
            FROM metadata_cache
            WHERE provider = @Provider AND payload_json IS NOT NULL;
            """,
            new { SqliteEpicCatalogCache.Provider },
            transaction: lease.Transaction,
            cancellationToken: ct));

        var keys = new Dictionary<string, EpicLaunchKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (Parse(row.CatalogItemId, row.PayloadJson) is { } key)
            {
                keys[row.CatalogItemId] = key;
            }
        }

        return keys;
    }

    /// <summary>
    /// Two fields out of the cached answer. A payload that is not an object, or
    /// is missing either field, or carries a value <see cref="EpicLaunchKey"/>
    /// does not recognise as an id, yields no key — this is cached network
    /// output, so it gets the same treatment as any other untrusted string.
    /// </summary>
    private static EpicLaunchKey? Parse(string catalogItemId, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return EpicLaunchKey.Create(
                Text(document.RootElement, "Namespace"),
                catalogItemId,
                Text(document.RootElement, "AppName"));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private sealed record Row(string CatalogItemId, string? PayloadJson);
}
