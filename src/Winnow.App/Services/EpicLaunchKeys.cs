using System.Text.Json;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Data;
using Winnow.Ingest.Epic;

namespace Winnow.App.Services;

/// <summary>
/// Resolves Epic's composite launch key by unioning two <c>metadata_cache</c>
/// providers: the local rows ingest writes from <c>catcache.bin</c>
/// (<see cref="SqliteEpicLaunchKeyStore"/>), and the rows the authenticated
/// catalog backfill writes (<see cref="SqliteEpicCatalogCache"/>). Local wins
/// where both hold the same item.
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

        // ORDER BY carries the precedence, so the loop below needs no
        // conditional: the remote rows land first and the local ones overwrite
        // them. SqliteEpicLaunchKeyStore.Provider sorts after
        // SqliteEpicCatalogCache.Provider on this comparison, and the CASE
        // states that rather than relying on how the two literals happen to
        // collate.
        var rows = await lease.Connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT provider_id AS CatalogItemId, payload_json AS PayloadJson
            FROM metadata_cache
            WHERE provider IN (@RemoteProvider, @LocalProvider) AND payload_json IS NOT NULL
            ORDER BY CASE provider WHEN @LocalProvider THEN 1 ELSE 0 END;
            """,
            new
            {
                RemoteProvider = SqliteEpicCatalogCache.Provider,
                LocalProvider = SqliteEpicLaunchKeyStore.Provider,
            },
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

    /// <summary>Extracts namespace and AppName from the cached JSON payload. Returns null for invalid or incomplete data.</summary>
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

/// <summary>
/// Persists the local half of the Epic launch key data. Lives in the composition
/// root rather than in the Epic module for the same reason
/// <see cref="SqliteEpicCatalogCache"/> does: <c>Winnow.Ingest.Epic</c> does not
/// reference <c>Winnow.Data</c>.
/// </summary>
public interface IEpicLaunchKeyStore
{
    /// <summary>
    /// Upserts one <c>metadata_cache</c> row per triple. Idempotent: a second
    /// save with the same catalog item id overwrites the payload.
    /// </summary>
    /// <param name="triples">Launch triples from the most recent scan. Empty is a no-op.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(IReadOnlyCollection<EpicLaunchTriple> triples, CancellationToken ct = default);
}

/// <inheritdoc cref="IEpicLaunchKeyStore"/>
public sealed class SqliteEpicLaunchKeyStore : IEpicLaunchKeyStore
{
    /// <summary>
    /// Deliberately a different provider from <see cref="SqliteEpicCatalogCache.Provider"/>.
    /// Those two share a <c>(provider, provider_id)</c> primary key space keyed on
    /// catalog item id, and the <c>epic-catalog</c> row holds a whole catalog item
    /// that the Epic web client reads back; a two-field local payload written over
    /// it would corrupt that cache.
    /// </summary>
    public const string Provider = "epic-local-launch";

    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _timeProvider;

    public SqliteEpicLaunchKeyStore(ISqliteConnectionFactory factory, TimeProvider? timeProvider = null)
    {
        _factory = factory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task SaveAsync(
        IReadOnlyCollection<EpicLaunchTriple> triples, CancellationToken ct = default)
    {
        if (triples.Count == 0)
        {
            return;
        }

        var fetchedAt = _timeProvider.GetUtcNow().UtcDateTime;

        // The property names are SqliteEpicLaunchKeys.Parse's, which are the
        // names the remote catalog payload already uses. One parser reads both
        // providers because both payloads spell these two fields the same way.
        var rows = triples.Select(triple => new
        {
            Provider,
            CatalogItemId = triple.CatalogItemId,
            PayloadJson = JsonSerializer.Serialize(new EpicLocalLaunchPayload(
                triple.CatalogNamespace, triple.AppName)),
            FetchedAt = fetchedAt,
        });

        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@Provider, @CatalogItemId, @PayloadJson, @FetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            rows,
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    /// <summary>
    /// The two fields, spelled the way the remote catalog payload already spells
    /// them, so one parser (<see cref="SqliteEpicLaunchKeys"/>) serves both providers.
    /// </summary>
    private sealed record EpicLocalLaunchPayload(string Namespace, string AppName);
}
