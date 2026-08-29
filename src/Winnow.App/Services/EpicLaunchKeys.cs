using System.Text.Json;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Data;

namespace Winnow.App.Services;

/// <summary>
/// Resolves Epic's composite launch key (namespace:catalogItemId:artifactId) for
/// cached catalog items, by reading the <c>metadata_cache</c> table that
/// <see cref="SqliteEpicCatalogCache"/> writes. Coverage is partial until the
/// catalog backfill completes; missing items render as no Play button.
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
