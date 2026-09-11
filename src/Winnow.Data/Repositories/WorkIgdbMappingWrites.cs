using System.Globalization;
using Dapper;
using Winnow.Core.Domain;

namespace Winnow.Data.Repositories;

/// <summary>A user mapping transition, composed inside the caller's atomic repository batch.</summary>
internal static class WorkIgdbMappingWrites
{
    internal static async Task<bool> TransitionAsync(
        DbLease lease, long workId, long? igdbId, bool pin, long expectedRevision, DateTime now, CancellationToken ct)
    {
        var current = await lease.Connection.QuerySingleOrDefaultAsync<Mapping>(new CommandDefinition("""
            SELECT igdb_id AS IgdbId, igdb_mapping_revision AS Revision FROM works WHERE id = @workId;
            """, new { workId }, lease.Transaction, cancellationToken: ct));
        if (current is null)
        {
            return false;
        }

        if (current.Revision != expectedRevision)
        {
            throw ManualIdentifierWrites.Conflict(ExternalIdProviders.Igdb, ManualEntryConflictReason.MappingChanged);
        }

        var providerId = igdbId?.ToString(CultureInfo.InvariantCulture);
        await ManualIdentifierWrites.AssertAvailableAsync(lease, workId, ExternalIdProviders.Igdb, providerId, ct);

        if (current.IgdbId != igdbId)
        {
            var manualEntries = await lease.Connection.QueryAsync<ManualLocation>(new CommandDefinition("""
                SELECT m.ownership_id AS OwnershipId, o.release_id AS ReleaseId
                FROM manual_entries m JOIN ownerships o ON o.id = m.ownership_id
                JOIN releases r ON r.id = o.release_id WHERE r.work_id = @workId;
                """, new { workId }, lease.Transaction, cancellationToken: ct));
            foreach (var entry in manualEntries)
            {
                await ManualIdentifierWrites.ReplaceAsync(lease, entry.OwnershipId, entry.ReleaseId,
                    workId, ExternalIdProviders.Igdb, providerId, now, ct);
            }

            // A typed identifier supplies no replacement metadata. Preserve
            // user fields and unknown legacy sources; clear values explicitly
            // supplied by the old IGDB mapping so they can be fetched again.
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                WITH igdb_fields AS (
                    SELECT field FROM work_field_sources
                    WHERE work_id = @workId AND source = 'igdb'
                )
                UPDATE works SET
                    first_release_year = CASE WHEN 'first_release_year' IN igdb_fields
                        THEN NULL ELSE first_release_year END,
                    summary = CASE WHEN 'summary' IN igdb_fields THEN NULL ELSE summary END,
                    cover_url = CASE WHEN 'cover_url' IN igdb_fields THEN NULL ELSE cover_url END,
                    publisher = CASE WHEN 'publisher' IN igdb_fields THEN NULL ELSE publisher END,
                    igdb_game_type = NULL, igdb_parent_id = NULL, igdb_version_parent_id = NULL
                WHERE id = @workId;
                DELETE FROM work_field_sources WHERE work_id = @workId AND source = 'igdb'
                  AND field IN ('first_release_year','summary','cover_url','publisher');
                """, new { workId }, lease.Transaction, cancellationToken: ct));
        }

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE work_igdb_pins SET cleared_at = @now WHERE work_id = @workId AND cleared_at IS NULL;
            """, new { workId, now }, lease.Transaction, cancellationToken: ct));
        if (pin && igdbId is not null)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO work_igdb_pins(work_id,igdb_id,pinned_at) VALUES(@workId,@igdbId,@now);
                """, new { workId, igdbId, now }, lease.Transaction, cancellationToken: ct));
        }

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works SET igdb_id = @igdbId, igdb_mapping_revision = igdb_mapping_revision + 1
            WHERE id = @workId AND igdb_mapping_revision = @expectedRevision;
            """, new { workId, igdbId, expectedRevision }, lease.Transaction, cancellationToken: ct));
        return true;
    }

    private sealed record Mapping(long? IgdbId, long Revision);
    private sealed record ManualLocation(long OwnershipId, long ReleaseId);
}
