using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class ReleaseEditionEvidenceRepository(ISqliteConnectionFactory factory, TimeProvider? time = null)
    : IReleaseEditionEvidenceRepository
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ReleaseEditionEvidence?> RecordAsync(ReleaseEditionEvidence evidence, CancellationToken ct = default)
    {
        if (evidence.EditionGameId <= 0 || evidence.VersionParentId <= 0
            || evidence.EditionGameId == evidence.VersionParentId || string.IsNullOrWhiteSpace(evidence.VersionTitle)
            || evidence.Sources.Count == 0 || evidence.ValidUntilUtc <= _time.GetUtcNow().UtcDateTime) return null;
        using var batch = new RepositoryWriteBatch(factory);
        var lease = batch.Lease;
        if (!await MatchesCurrentInputsAsync(lease, evidence, ct)) return null;
        var sources = SerializeSources(evidence.Sources);
        var parameters = new
        {
            evidence.ReleaseId, evidence.WorkId, evidence.Provider, evidence.ProviderId,
            evidence.EditionGameId, evidence.VersionParentId, evidence.VersionTitle,
            sources, validUntil = evidence.ValidUntilUtc, now = _time.GetUtcNow().UtcDateTime,
        };
        var prior = await lease.Connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("""
            SELECT id FROM release_edition_evidence
            WHERE release_id = @ReleaseId AND work_id = @WorkId
              AND provider = @Provider AND provider_id = @ProviderId
              AND edition_game_id = @EditionGameId AND version_parent_id = @VersionParentId
              AND version_title = @VersionTitle AND sources_json = @sources AND valid_until = @validUntil
            ORDER BY id DESC LIMIT 1;
            """, parameters, lease.Transaction, cancellationToken: ct));
        var id = prior ?? await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO release_edition_evidence
                (release_id, work_id, provider, provider_id, edition_game_id, version_parent_id,
                 version_title, sources_json, valid_until, observed_at)
            VALUES (@ReleaseId, @WorkId, @Provider, @ProviderId, @EditionGameId, @VersionParentId,
                    @VersionTitle, @sources, @validUntil, @now)
            RETURNING id;
            """, parameters, lease.Transaction, cancellationToken: ct));
        batch.Commit();
        return evidence with { Id = id };
    }

    internal static async Task<bool> IsCurrentAsync(DbLease lease, ReleaseEditionEvidence evidence, DateTime now, CancellationToken ct)
    {
        if (evidence.Id <= 0 || evidence.ValidUntilUtc <= now) return false;
        var persisted = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM release_edition_evidence
            WHERE id = @Id AND release_id = @ReleaseId AND work_id = @WorkId
              AND provider = @Provider AND provider_id = @ProviderId
              AND edition_game_id = @EditionGameId AND version_parent_id = @VersionParentId
              AND version_title = @VersionTitle AND sources_json = @sources AND valid_until = @validUntil;
            """, new { evidence.Id, evidence.ReleaseId, evidence.WorkId, evidence.Provider, evidence.ProviderId,
                evidence.EditionGameId, evidence.VersionParentId, evidence.VersionTitle,
                sources = SerializeSources(evidence.Sources), validUntil = evidence.ValidUntilUtc },
            lease.Transaction, cancellationToken: ct));
        return persisted == 1 && await MatchesCurrentInputsAsync(lease, evidence, ct);
    }

    private static async Task<bool> MatchesCurrentInputsAsync(DbLease lease, ReleaseEditionEvidence evidence, CancellationToken ct)
    {
        var matches = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM releases release JOIN external_ids external ON external.release_id = release.id
            WHERE release.id = @ReleaseId AND release.work_id = @WorkId
              AND external.provider = @Provider AND external.provider_id = @ProviderId;
            """, evidence, lease.Transaction, cancellationToken: ct));
        if (matches != 1) return false;
        foreach (var source in evidence.Sources)
        {
            var payload = await lease.Connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("""
                SELECT payload_json FROM metadata_cache WHERE provider = @Provider AND provider_id = @Key;
                """, source, lease.Transaction, cancellationToken: ct));
            if (CachedEvidenceSource.FromPayload(source.Provider, source.Key, payload) != source) return false;
        }
        return true;
    }

    internal static string SerializeSources(IReadOnlyList<CachedEvidenceSource> sources)
        => JsonSerializer.Serialize(sources.ToArray(), EditionEvidenceJsonContext.Default.CachedEvidenceSourceArray);
}

[JsonSerializable(typeof(CachedEvidenceSource[]))]
internal partial class EditionEvidenceJsonContext : JsonSerializerContext;
