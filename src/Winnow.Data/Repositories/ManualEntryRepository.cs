using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>manual_entries</c> (migration 0025) and the work, release
/// and ownership rows it creates. A hand-added game is an ordinary work +
/// release + ownership whose store is <c>manual</c> and whose
/// <c>manual_entries</c> row exists — one mechanism, not two, and a table no
/// ingest path writes.
/// </summary>
public sealed class ManualEntryRepository : IManualEntryRepository
{
    private const string Columns = """
        m.ownership_id    AS OwnershipId,
        o.release_id      AS ReleaseId,
        r.work_id         AS WorkId,
        w.name            AS Title,
        w.first_release_year AS FirstReleaseYear,
        m.platform_label  AS PlatformLabel,
        m.executable_path AS ExecutablePath,
        o.install_path    AS InstallPath,
        m.added_at        AS AddedAt,
        m.updated_at      AS UpdatedAt
        """;

    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public ManualEntryRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ManualEntry> CreateAsync(ManualGameDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var title = draft.TrimmedTitle;
        if (title.Length == 0)
        {
            throw new ArgumentException("A hand-added game needs a title.", nameof(draft));
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        await AssertIdentifiersAreFreeAsync(lease, draft, workIdBeingEdited: null, ct);

        // name_is_provisional = 0 is the whole of the "ingest never overwrites
        // it" guarantee on the title side. ExternalIdResolver
        // .PromoteProvisionalNameAsync renames a work only while the flag is
        // set, and EnrichmentSyncService's patch is fill-only, so a hand-typed
        // name is never a candidate for replacement.
        var workId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works (igdb_id, name, sort_name, first_release_year, name_is_provisional)
            VALUES (@igdbId, @title, @title, @year, 0)
            RETURNING id;
            """,
            new { igdbId = draft.IgdbId, title, year = draft.FirstReleaseYear },
            lease.Transaction, cancellationToken: ct));

        var releaseId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO releases (work_id, name, platform)
            VALUES (@workId, @title, @platform)
            RETURNING id;
            """,
            new { workId, title, platform = Trimmed(draft.PlatformLabel) },
            lease.Transaction, cancellationToken: ct));

        await InsertExternalIdsAsync(lease, releaseId, draft, ct);

        // Store 'manual' is the other half of the guarantee.
        // OwnershipRepository.UpsertAsync conflicts on (release_id, store),
        // and no ingest reader emits this store, so no ingest pass can reach
        // this row however it resolves.
        var installPath = draft.EffectiveInstallPath;
        var ownershipId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships (release_id, store, install_path, installed)
            VALUES (@releaseId, @store, @installPath, @installed)
            RETURNING id;
            """,
            new
            {
                releaseId,
                store = OwnershipStores.Manual,
                installPath,
                installed = installPath is not null,
            },
            lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO manual_entries (ownership_id, executable_path, platform_label, added_at, updated_at)
            VALUES (@ownershipId, @executablePath, @platformLabel, @now, @now);
            """,
            new
            {
                ownershipId,
                executablePath = Trimmed(draft.ExecutablePath),
                platformLabel = Trimmed(draft.PlatformLabel),
                now,
            },
            lease.Transaction, cancellationToken: ct));

        scope.Commit();

        return new ManualEntry
        {
            OwnershipId = ownershipId,
            ReleaseId = releaseId,
            WorkId = workId,
            Title = title,
            FirstReleaseYear = draft.FirstReleaseYear,
            PlatformLabel = Trimmed(draft.PlatformLabel),
            ExecutablePath = Trimmed(draft.ExecutablePath),
            InstallPath = installPath,
            AddedAt = now,
            UpdatedAt = now,
        };
    }

    public async Task<ManualEntry?> GetAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<ManualEntry>(new CommandDefinition($"""
            SELECT {Columns}
            FROM manual_entries m
            JOIN ownerships o ON o.id = m.ownership_id
            JOIN releases   r ON r.id = o.release_id
            JOIN works      w ON w.id = r.work_id
            WHERE m.ownership_id = @ownershipId;
            """, new { ownershipId }, lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ManualEntry>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ManualEntry>(new CommandDefinition($"""
            SELECT {Columns}
            FROM manual_entries m
            JOIN ownerships o ON o.id = m.ownership_id
            JOIN releases   r ON r.id = o.release_id
            JOIN works      w ON w.id = r.work_id
            ORDER BY w.name, m.ownership_id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<bool> UpdateAsync(
        long ownershipId, ManualGameDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var title = draft.TrimmedTitle;
        if (title.Length == 0)
        {
            throw new ArgumentException("A hand-added game needs a title.", nameof(draft));
        }

        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var located = await lease.Connection.QuerySingleOrDefaultAsync<EntryLocation>(
            new CommandDefinition("""
                SELECT o.release_id AS ReleaseId, r.work_id AS WorkId
                FROM manual_entries m
                JOIN ownerships o ON o.id = m.ownership_id
                JOIN releases   r ON r.id = o.release_id
                WHERE m.ownership_id = @ownershipId;
                """, new { ownershipId }, lease.Transaction, cancellationToken: ct));

        if (located is not { } row)
        {
            return false;
        }

        await AssertIdentifiersAreFreeAsync(lease, draft, row.WorkId, ct);

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works
            SET igdb_id = @igdbId,
                name = @title,
                sort_name = @title,
                first_release_year = @year,
                name_is_provisional = 0
            WHERE id = @workId;
            """,
            new { igdbId = draft.IgdbId, title, year = draft.FirstReleaseYear, workId = row.WorkId },
            lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE releases
            SET name = @title, platform = @platform
            WHERE id = @releaseId;
            """,
            new { title, platform = Trimmed(draft.PlatformLabel), releaseId = row.ReleaseId },
            lease.Transaction, cancellationToken: ct));

        // The release keeps whatever external ids it already had and gains any
        // the edit adds. Removing one would strand a store entry that had since
        // attached to the same release.
        await InsertExternalIdsAsync(lease, row.ReleaseId, draft, ct);

        var installPath = draft.EffectiveInstallPath;
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ownerships
            SET install_path = @installPath, installed = @installed
            WHERE id = @ownershipId;
            """,
            new { installPath, installed = installPath is not null, ownershipId },
            lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE manual_entries
            SET executable_path = @executablePath,
                platform_label = @platformLabel,
                updated_at = @now
            WHERE ownership_id = @ownershipId;
            """,
            new
            {
                executablePath = Trimmed(draft.ExecutablePath),
                platformLabel = Trimmed(draft.PlatformLabel),
                now = _clock.GetUtcNow().UtcDateTime,
                ownershipId,
            },
            lease.Transaction, cancellationToken: ct));

        scope.Commit();
        return true;
    }

    public async Task<bool> DeleteAsync(long ownershipId, CancellationToken ct = default)
    {
        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var located = await lease.Connection.QuerySingleOrDefaultAsync<EntryLocation>(
            new CommandDefinition("""
                SELECT o.release_id AS ReleaseId, r.work_id AS WorkId
                FROM manual_entries m
                JOIN ownerships o ON o.id = m.ownership_id
                JOIN releases   r ON r.id = o.release_id
                WHERE m.ownership_id = @ownershipId;
                """, new { ownershipId }, lease.Transaction, cancellationToken: ct));

        if (located is not { } row)
        {
            return false;
        }

        // Three deletes in order, each narrowed by what is left. The ownership
        // goes (manual_entries follows it by cascade); the release goes only
        // when no other store entry hangs off it, and the work only when it has
        // no releases left. A Steam entry that later attached to the same
        // release by an external id the user supplied keeps its game.
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ownerships WHERE id = @ownershipId;",
            new { ownershipId }, lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM releases
            WHERE id = @releaseId
              AND NOT EXISTS (SELECT 1 FROM ownerships WHERE release_id = @releaseId);
            """, new { releaseId = row.ReleaseId }, lease.Transaction, cancellationToken: ct));

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM works
            WHERE id = @workId
              AND NOT EXISTS (SELECT 1 FROM releases WHERE work_id = @workId);
            """, new { workId = row.WorkId }, lease.Transaction, cancellationToken: ct));

        scope.Commit();
        return true;
    }

    private sealed record EntryLocation(long ReleaseId, long WorkId);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task InsertExternalIdsAsync(
        DbLease lease, long releaseId, ManualGameDraft draft, CancellationToken ct)
    {
        var steamAppId = Trimmed(draft.SteamAppId);
        if (steamAppId is not null)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO external_ids (release_id, provider, provider_id)
                VALUES (@releaseId, @provider, @providerId)
                ON CONFLICT (provider, provider_id) DO NOTHING;
                """,
                new { releaseId, provider = ExternalIdProviders.Steam, providerId = steamAppId },
                lease.Transaction, cancellationToken: ct));
        }

        if (draft.IgdbId is { } igdbId)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO external_ids (release_id, provider, provider_id)
                VALUES (@releaseId, @provider, @providerId)
                ON CONFLICT (provider, provider_id) DO NOTHING;
                """,
                new
                {
                    releaseId,
                    provider = ExternalIdProviders.Igdb,
                    providerId = igdbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                lease.Transaction, cancellationToken: ct));
        }
    }

    // Checked before the insert rather than caught after it, because "this
    // IGDB id already belongs to another game in your library" is a sentence
    // the add form can show and a UNIQUE constraint violation is not.
    private static async Task AssertIdentifiersAreFreeAsync(
        DbLease lease, ManualGameDraft draft, long? workIdBeingEdited, CancellationToken ct)
    {
        if (draft.IgdbId is { } igdbId)
        {
            var owner = await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT id FROM works WHERE igdb_id = @igdbId;",
                new { igdbId }, lease.Transaction, cancellationToken: ct));

            if (owner is not null && owner != workIdBeingEdited)
            {
                throw new ManualEntryConflictException(
                    nameof(ManualGameDraft.IgdbId),
                    $"IGDB id {igdbId} already belongs to another game in this library.");
            }
        }

        var steamAppId = Trimmed(draft.SteamAppId);
        if (steamAppId is not null)
        {
            var ownerWorkId = await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT r.work_id
                FROM external_ids e
                JOIN releases r ON r.id = e.release_id
                WHERE e.provider = @provider AND e.provider_id = @providerId;
                """,
                new { provider = ExternalIdProviders.Steam, providerId = steamAppId },
                lease.Transaction, cancellationToken: ct));

            if (ownerWorkId is not null && ownerWorkId != workIdBeingEdited)
            {
                throw new ManualEntryConflictException(
                    nameof(ManualGameDraft.SteamAppId),
                    $"Steam appid {steamAppId} already belongs to another game in this library.");
            }
        }
    }
}
