using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class AchievementRepository(ISqliteConnectionFactory factory) : IAchievementRepository
{
    public async Task<IReadOnlyList<SteamAchievementCandidate>> GetDueSteamAsync(string accountRef, DateTime asOfUtc,
        int limit, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return (await lease.Connection.QueryAsync<SteamAchievementCandidate>(new CommandDefinition("""
            SELECT DISTINCT o.release_id AS ReleaseId,e.provider_id AS AppId
            FROM ownerships o JOIN ownership_accounts a ON a.ownership_id=o.id AND a.account_ref=@accountRef
            JOIN external_ids e ON e.release_id=o.release_id AND e.provider='steam'
            LEFT JOIN achievement_observations s ON s.release_id=o.release_id AND s.account_ref=@accountRef
            WHERE o.store='steam' AND (s.attempted_at IS NULL OR s.attempted_at<=@cutoff)
                AND NOT EXISTS(SELECT 1 FROM external_ids x WHERE x.provider='steam'
                    AND ((x.release_id=e.release_id AND x.provider_id<>e.provider_id)
                        OR (x.release_id<>e.release_id AND x.provider_id=e.provider_id)))
            ORDER BY s.attempted_at,o.release_id LIMIT @limit;
            """, new { accountRef, cutoff = asOfUtc - ReleaseAchievementSummary.Freshness, limit = Math.Clamp(limit, 1, 100) },
            lease.Transaction, cancellationToken: ct))).AsList();
    }

    public async Task SaveAsync(long releaseId, string accountRef, AchievementFetch result, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountRef);
        if (result.AttemptedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Achievement time must be UTC.");
        if (result.Schema is null && (result.Unlocks is not null || result.GlobalPercentages is not null))
            throw new ArgumentException("Progress needs the corresponding achievement schema.");
        if (result.Schema is { } definitions)
        {
            var keys = definitions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            if (keys.Count != definitions.Count || keys.Any(string.IsNullOrWhiteSpace)
                || result.Unlocks?.Any(item => !keys.Contains(item.Key) || item.Value > result.AttemptedAt) == true
                || result.GlobalPercentages?.Any(item => !keys.Contains(item.Key) || !double.IsFinite(item.Value) || item.Value is < 0 or > 100) == true)
                throw new ArgumentException("Achievement data does not match its schema.");
        }
        using var scope = factory.Begin();
        using var lease = factory.Lease();
        var prior = await lease.Connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT attempted_at FROM achievement_observations WHERE release_id=@releaseId AND account_ref=@accountRef;",
            new { releaseId, accountRef }, lease.Transaction, cancellationToken: ct));
        if (prior > result.AttemptedAt) { scope.Commit(); return; }
        async Task Execute(string sql, object args) => await lease.Connection.ExecuteAsync(
            new CommandDefinition(sql, args, lease.Transaction, cancellationToken: ct));

        var preserveSharedSchema = false;
        if (result.Schema is { } incoming)
        {
            var storedKeys = (await lease.Connection.QueryAsync<string>(new CommandDefinition(
                "SELECT provider_key FROM achievements WHERE release_id=@releaseId;", new { releaseId },
                lease.Transaction, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
            var latestSchema = await lease.Connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT MAX(schema_at) FROM achievement_observations WHERE release_id=@releaseId;", new { releaseId },
                lease.Transaction, cancellationToken: ct));
            var latestGlobal = await lease.Connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
                "SELECT MAX(global_at) FROM achievement_observations WHERE release_id=@releaseId;", new { releaseId },
                lease.Transaction, cancellationToken: ct));
            preserveSharedSchema = latestSchema > result.AttemptedAt;
            if (latestGlobal > result.AttemptedAt) result = result with { GlobalPercentages = null };
            if (!storedKeys.SetEquals(incoming.Select(item => item.Key)))
            {
                // A new denominator beside an old unlock set is not the prior valid percentage.
                if (preserveSharedSchema || (incoming.Count > 0 && storedKeys.Count > 0 && result.Unlocks is null))
                    result = result with { Schema = null, Unlocks = null, GlobalPercentages = null };
                else
                    await Execute("""
                        UPDATE achievement_observations SET progress_at=NULL, schema_at=@at, availability=@availability
                        WHERE release_id=@releaseId AND account_ref<>@accountRef;
                        """, new { releaseId, accountRef, at = result.AttemptedAt,
                            availability = incoming.Count == 0 ? AchievementAvailability.NoSchema : AchievementAvailability.Unknown });
            }
        }
        if (result.Schema is { } schema)
        {
            if (!preserveSharedSchema)
            {
                foreach (var item in schema)
                    await Execute("""
                        INSERT INTO achievements(release_id,provider_key,name,description,hidden)
                        VALUES(@releaseId,@Key,@Name,@Description,@Hidden)
                        ON CONFLICT(release_id,provider_key) DO UPDATE SET
                            name=excluded.name,description=excluded.description,hidden=excluded.hidden;
                        """, new { releaseId, item.Key, item.Name, item.Description, item.Hidden });
                await Execute("DELETE FROM achievements WHERE release_id=@releaseId AND provider_key NOT IN @keys;",
                    new { releaseId, keys = schema.Select(a => a.Key).ToArray() });
            }
            if (result.Unlocks is { } unlocks)
            {
                await Execute("DELETE FROM account_achievement_unlocks WHERE release_id=@releaseId AND account_ref=@accountRef;",
                    new { releaseId, accountRef });
                foreach (var item in unlocks)
                    await Execute("""
                        INSERT INTO account_achievement_unlocks(release_id,provider_key,account_ref,unlocked_at)
                        VALUES(@releaseId,@key,@accountRef,@at);
                        """, new { releaseId, key = item.Key, accountRef, at = item.Value });
            }
            if (result.GlobalPercentages is { } globals)
                foreach (var item in globals)
                    await Execute("UPDATE achievements SET global_pct=@pct WHERE release_id=@releaseId AND provider_key=@key;",
                        new { releaseId, key = item.Key, pct = item.Value });
        }
        var noSchema = result.Schema is { Count: 0 };
        var availability = result.Schema is null || (!noSchema && result.Unlocks is null)
            ? AchievementAvailability.Unavailable
            : noSchema ? AchievementAvailability.NoSchema : AchievementAvailability.Available;
        await Execute("""
            INSERT INTO achievement_observations(release_id,account_ref,availability,attempted_at,schema_at,progress_at,global_at)
            VALUES(@releaseId,@accountRef,@availability,@at,@schemaAt,@progressAt,@globalAt)
            ON CONFLICT(release_id,account_ref) DO UPDATE SET
                availability=excluded.availability,attempted_at=excluded.attempted_at,
                schema_at=COALESCE(excluded.schema_at,schema_at),
                progress_at=CASE WHEN @noSchema THEN NULL ELSE COALESCE(excluded.progress_at,progress_at) END,
                global_at=COALESCE(excluded.global_at,global_at);
            """, new
            {
                releaseId, accountRef, availability, at = result.AttemptedAt, noSchema,
                schemaAt = result.Schema is null ? (DateTime?)null : result.AttemptedAt,
                progressAt = result.Unlocks is null || noSchema ? (DateTime?)null : result.AttemptedAt,
                globalAt = result.GlobalPercentages is null ? (DateTime?)null : result.AttemptedAt,
            });
        scope.Commit();
    }

    public async Task<IReadOnlyList<ReleaseAchievementSummary>> GetForAccountAsync(
        IReadOnlyList<long> releaseIds, string? accountRef, DateTime asOfUtc, CancellationToken ct = default)
    {
        if (releaseIds.Count == 0) return [];
        using var lease = factory.Lease();
        var rows = await lease.Connection.QueryAsync<ReleaseAchievementSummary>(new CommandDefinition("""
            SELECT r.id AS ReleaseId,@accountRef AS AccountRef,
                (SELECT COUNT(*) FROM achievements a WHERE a.release_id=r.id) AS Total,
                (SELECT COUNT(*) FROM account_achievement_unlocks u
                    WHERE u.release_id=r.id AND u.account_ref=@accountRef) AS Unlocked,
                COALESCE(o.availability,0) AS Availability,
                o.progress_at AS ObservedAt,o.attempted_at AS LastAttemptAt,
                o.schema_at AS SchemaObservedAt,o.global_at AS GlobalObservedAt,
                CASE WHEN o.progress_at IS NOT NULL THEN 1 ELSE 0 END AS HasKnownProgress
            FROM releases r LEFT JOIN achievement_observations o ON o.release_id=r.id AND o.account_ref=@accountRef
            WHERE r.id IN @releaseIds ORDER BY r.id;
            """, new { releaseIds, accountRef }, lease.Transaction, cancellationToken: ct));
        return rows.Select(row => row with
        {
            IsStale = row.HasKnownProgress && (row.Availability != AchievementAvailability.Available
                || row.ObservedAt < asOfUtc - ReleaseAchievementSummary.Freshness),
        }).ToArray();
    }
}
