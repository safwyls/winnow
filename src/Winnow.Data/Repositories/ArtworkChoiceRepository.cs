using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class ArtworkChoiceRepository(ISqliteConnectionFactory factory) : IArtworkChoiceRepository
{
    private const string Columns = """
        work_id AS WorkId, slot AS Slot, kind AS Kind,
        asset_key AS AssetKey, source_id AS SourceId, asset_id AS AssetId,
        source_url AS SourceUrl, creator AS Creator, page_url AS PageUrl,
        collection_id AS CollectionId, revision AS Revision
        """;

    public async Task<IReadOnlyList<ArtworkChoice>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return (await lease.Connection.QueryAsync<ArtworkChoice>(new CommandDefinition(
            $"SELECT {Columns} FROM artwork_choices ORDER BY revision;",
            transaction: lease.Transaction, cancellationToken: ct))).AsList();
    }

    public async Task<ArtworkChoice?> GetEffectiveAsync(
        IReadOnlyCollection<long> workIds, ArtworkSlot slot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workIds);
        if (workIds.Count == 0) return null;
        using var lease = factory.Lease();
        var choices = await lease.Connection.QueryAsync<ArtworkChoice>(new CommandDefinition(
            $"SELECT {Columns} FROM artwork_choices WHERE work_id IN @workIds AND slot = @slot;",
            new { workIds, slot }, transaction: lease.Transaction, cancellationToken: ct));
        return ArtworkChoices.Effective(choices, workIds, slot);
    }

    public async Task<long> SetAsync(ArtworkChoice choice, CancellationToken ct = default)
    {
        Validate(choice);
        using var batch = new RepositoryWriteBatch(factory);
        var revision = await WriteAsync(batch.Lease, choice, ct);
        batch.Commit();
        return revision;
    }

    public async Task ResetAsync(
        IReadOnlyCollection<long> workIds, ArtworkSlot slot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workIds);
        if (workIds.Count == 0) return;
        using var batch = new RepositoryWriteBatch(factory);
        await batch.Lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM artwork_choices WHERE work_id IN @workIds AND slot = @slot;",
            new { workIds, slot }, transaction: batch.Lease.Transaction, cancellationToken: ct));
        var legacyField = slot switch
        {
            ArtworkSlot.Hero => "background_url",
            ArtworkSlot.Cover => "cover_url",
            _ => null
        };
        if (legacyField is not null)
        {
            // Migrated imports still occupy the old metadata field. Clear only
            // user-owned values so resetting cannot reveal the same image again.
            await batch.Lease.Connection.ExecuteAsync(new CommandDefinition($"""
                UPDATE works SET {legacyField} = NULL
                WHERE id IN @workIds AND EXISTS (
                    SELECT 1 FROM work_field_sources s
                    WHERE s.work_id = works.id AND s.field = @legacyField AND s.source = 'user'
                );
                DELETE FROM work_field_sources
                WHERE work_id IN @workIds AND field = @legacyField AND source = 'user';
                """, new { workIds, legacyField },
                transaction: batch.Lease.Transaction, cancellationToken: ct));
        }
        batch.Commit();
    }

    public async Task<bool> TryRestoreAsync(
        ArtworkChoice written, ArtworkChoice? preceding, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(written);
        if (preceding is not null)
        {
            Validate(preceding);
            if (preceding.WorkId != written.WorkId || preceding.Slot != written.Slot || preceding.Kind != written.Kind)
                throw new ArgumentException("Restoration must target the same original work, slot and kind.", nameof(preceding));
        }

        using var batch = new RepositoryWriteBatch(factory);
        var deleted = await batch.Lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM artwork_choices
            WHERE work_id = @WorkId AND slot = @Slot AND kind = @Kind AND revision = @Revision;
            """, written, transaction: batch.Lease.Transaction, cancellationToken: ct));
        if (deleted != 0 && preceding is not null)
            await WriteAsync(batch.Lease, preceding, ct);
        batch.Commit();
        return deleted != 0;
    }

    private static async Task<long> WriteAsync(DbLease lease, ArtworkChoice choice, CancellationToken ct)
    {
        // Delete and insert inside the same batch so every replacement receives a
        // globally increasing revision, even after all prior choices were reset.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM artwork_choices WHERE work_id = @WorkId AND slot = @Slot AND kind = @Kind;
            """, choice, transaction: lease.Transaction, cancellationToken: ct));
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO artwork_choices
                (work_id, slot, kind, asset_key, source_id, asset_id, source_url, creator, page_url, collection_id)
            VALUES
                (@WorkId, @Slot, @Kind, @AssetKey, @SourceId, @AssetId, @SourceUrl, @Creator, @PageUrl, @CollectionId)
            RETURNING revision;
            """, choice, transaction: lease.Transaction, cancellationToken: ct));
    }

    private static void Validate(ArtworkChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(choice.WorkId);
        if (!Enum.IsDefined(choice.Slot)) throw new ArgumentOutOfRangeException(nameof(choice.Slot));
        if (!Enum.IsDefined(choice.Kind)) throw new ArgumentOutOfRangeException(nameof(choice.Kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.AssetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice.AssetId);
    }
}
