using Dapper;
using Winnow.Core.Domain;

namespace Winnow.Data.Repositories;

/// <summary>Retracts known manual assertions without claiming that legacy identifiers were user-entered.</summary>
internal static class ManualIdentifierWrites
{
    internal static async Task ReplaceAsync(
        DbLease lease, long ownershipId, long releaseId, long workId, string provider,
        string? providerId, DateTime now, CancellationToken ct, bool creating = false)
    {
        var prior = await lease.Connection.QuerySingleOrDefaultAsync<Assertion>(new CommandDefinition("""
            SELECT id AS Id, provider_id AS ProviderId, owns_mapping AS OwnsMapping
            FROM manual_entry_identifiers
            WHERE ownership_id = @ownershipId AND provider = @provider AND retracted_at IS NULL;
            """, new { ownershipId, provider }, lease.Transaction, cancellationToken: ct));

        if (prior is not null && prior.ProviderId == providerId)
        {
            return;
        }

        if (prior is null && !creating)
        {
            var hasLegacyIds = await lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM external_ids WHERE release_id = @releaseId AND provider = @provider);
                """, new { releaseId, provider }, lease.Transaction, cancellationToken: ct));
            if (hasLegacyIds)
            {
                throw Conflict(provider, ManualEntryConflictReason.LegacyIdentifierHistory);
            }
        }

        await AssertAvailableAsync(lease, workId, provider, providerId, ct);

        if (prior is { ProviderId: not null, OwnsMapping: false })
        {
            // An assertion that reused another mapping cannot retract that
            // mapping. Refuse instead of reporting a correction while the old
            // identifier still resolves to this game.
            throw Conflict(provider, ManualEntryConflictReason.LegacyIdentifierHistory);
        }

        if (prior is { ProviderId: not null, OwnsMapping: true })
        {
            // A Steam ownership proves that another path now relies on this
            // identifier. A plugin ownership may carry cross-store ids too.
            // Retain both observations and refuse the correction explicitly.
            var observedElsewhere = await lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(
                    SELECT 1 FROM ownerships
                    WHERE release_id = @releaseId AND store <> 'manual'
                      AND (store = @provider OR store LIKE 'plugin:%')
                );
                """, new { releaseId, provider }, lease.Transaction, cancellationToken: ct));
            if (observedElsewhere)
            {
                throw Conflict(provider, ManualEntryConflictReason.StorefrontObservation);
            }

            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM external_ids
                WHERE release_id = @releaseId AND provider = @provider AND provider_id = @priorId
                  AND NOT EXISTS (
                      SELECT 1 FROM manual_entry_identifiers
                      WHERE provider = @provider AND provider_id = @priorId
                        AND retracted_at IS NULL AND id <> @priorAssertionId
                  );
                """, new { releaseId, provider, priorId = prior.ProviderId, priorAssertionId = prior.Id },
                lease.Transaction, cancellationToken: ct));
        }

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE manual_entry_identifiers SET retracted_at = @now
            WHERE ownership_id = @ownershipId AND provider = @provider AND retracted_at IS NULL;
            """, new { ownershipId, provider, now }, lease.Transaction, cancellationToken: ct));

        var ownsMapping = false;
        if (providerId is not null)
        {
            ownsMapping = await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO external_ids(release_id,provider,provider_id)
                VALUES(@releaseId,@provider,@providerId)
                ON CONFLICT(provider,provider_id) DO NOTHING;
                """, new { releaseId, provider, providerId }, lease.Transaction, cancellationToken: ct)) > 0;
        }

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO manual_entry_identifiers(
                ownership_id,provider,provider_id,owns_mapping,asserted_at)
            VALUES(@ownershipId,@provider,@providerId,@ownsMapping,@now);
            """, new { ownershipId, provider, providerId, ownsMapping, now },
            lease.Transaction, cancellationToken: ct));
    }

    internal static async Task AssertAvailableAsync(
        DbLease lease, long? workId, string provider, string? providerId, CancellationToken ct)
    {
        if (providerId is null)
        {
            return;
        }

        var conflict = await lease.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(
                SELECT 1 FROM external_ids e JOIN releases r ON r.id = e.release_id
                WHERE e.provider = @provider AND e.provider_id = @providerId
                  AND (@workId IS NULL OR r.work_id <> @workId)
            ) OR (@provider = 'igdb' AND (
                EXISTS(SELECT 1 FROM works WHERE igdb_id = CAST(@providerId AS INTEGER)
                       AND (@workId IS NULL OR id <> @workId))
                OR EXISTS(SELECT 1 FROM work_igdb_pins WHERE igdb_id = CAST(@providerId AS INTEGER)
                          AND cleared_at IS NULL AND (@workId IS NULL OR work_id <> @workId))
            ));
            """, new { workId, provider, providerId }, lease.Transaction, cancellationToken: ct));
        if (conflict)
        {
            throw Conflict(provider, ManualEntryConflictReason.ClaimedByAnotherGame);
        }
    }

    internal static ManualEntryConflictException Conflict(string provider, ManualEntryConflictReason reason)
        => new(provider == ExternalIdProviders.Igdb ? nameof(ManualGameDraft.IgdbId) : nameof(ManualGameDraft.SteamAppId),
            reason switch
            {
                ManualEntryConflictReason.LegacyIdentifierHistory => "The existing identifier has no recorded origin and cannot be replaced safely.",
                ManualEntryConflictReason.StorefrontObservation => "A storefront observation relies on the existing identifier.",
                ManualEntryConflictReason.MappingChanged => "The game's mapping changed after the edit was opened.",
                _ => "This identifier already belongs to another game in the library.",
            }) { Reason = reason };

    private sealed record Assertion
    {
        public long Id { get; init; }
        public string? ProviderId { get; init; }
        public bool OwnsMapping { get; init; }
    }
}
