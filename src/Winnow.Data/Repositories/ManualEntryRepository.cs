using System.Globalization;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>Manual entries, user metadata and retractable identifier assertions share one write boundary.</summary>
public sealed class ManualEntryRepository : IManualEntryRepository
{
    private const string Columns = """
        m.ownership_id AS OwnershipId,
        o.release_id AS ReleaseId,
        r.work_id AS WorkId,
        w.name AS Title,
        w.first_release_year AS FirstReleaseYear,
        w.igdb_id AS IgdbId,
        w.igdb_mapping_revision AS IgdbMappingRevision,
        CASE WHEN EXISTS(
            SELECT 1 FROM manual_entry_identifiers i
            WHERE i.ownership_id = m.ownership_id AND i.provider = 'steam' AND i.retracted_at IS NULL)
        THEN (
            SELECT i.provider_id FROM manual_entry_identifiers i
            WHERE i.ownership_id = m.ownership_id AND i.provider = 'steam' AND i.retracted_at IS NULL)
        ELSE (
            SELECT e.provider_id FROM external_ids e
            WHERE e.release_id = r.id AND e.provider = 'steam'
            ORDER BY e.provider_id LIMIT 1)
        END AS SteamAppId,
        m.platform_label AS PlatformLabel,
        m.executable_path AS ExecutablePath,
        o.install_path AS InstallPath,
        m.added_at AS AddedAt,
        m.updated_at AS UpdatedAt
        """;

    private const string From = """
        FROM manual_entries m
        JOIN ownerships o ON o.id = m.ownership_id
        JOIN releases r ON r.id = o.release_id
        JOIN works w ON w.id = r.work_id
        """;

    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;
    private readonly WorkFieldSourceRepository _fields;

    public ManualEntryRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
        _fields = new WorkFieldSourceRepository(factory, _clock);
    }

    public async Task<ManualEntry> CreateAsync(ManualGameDraft draft, CancellationToken ct = default)
    {
        draft = Validate(draft);
        var title = draft.TrimmedTitle;
        var now = _clock.GetUtcNow().UtcDateTime;

        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;
        await AssertIdentifiersAreFreeAsync(lease, draft, null, ct);

        var workId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO works(name,sort_name,name_is_provisional)
            VALUES(@title,@title,0) RETURNING id;
            """, new { title }, lease.Transaction, cancellationToken: ct));
        var releaseId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO releases(work_id,name,platform)
            VALUES(@workId,@title,@platform) RETURNING id;
            """, new { workId, title, platform = Trimmed(draft.PlatformLabel) },
            lease.Transaction, cancellationToken: ct));
        var installPath = draft.EffectiveInstallPath;
        var ownershipId = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships(release_id,store,install_path,installed)
            VALUES(@releaseId,'manual',@installPath,@installed) RETURNING id;
            """, new { releaseId, installPath, installed = installPath is not null },
            lease.Transaction, cancellationToken: ct));
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO manual_entries(ownership_id,executable_path,platform_label,added_at,updated_at)
            VALUES(@ownershipId,@executablePath,@platformLabel,@now,@now);
            """, new
            {
                ownershipId,
                executablePath = Trimmed(draft.ExecutablePath),
                platformLabel = Trimmed(draft.PlatformLabel),
                now,
            }, lease.Transaction, cancellationToken: ct));

        await ManualIdentifierWrites.ReplaceAsync(lease, ownershipId, releaseId, workId,
            ExternalIdProviders.Steam, draft.SteamAppId, now, ct, creating: true);
        await ManualIdentifierWrites.ReplaceAsync(lease, ownershipId, releaseId, workId,
            ExternalIdProviders.Igdb, null, now, ct, creating: true);
        if (draft.IgdbId is not null)
        {
            await WorkIgdbMappingWrites.TransitionAsync(lease, workId, draft.IgdbId,
                pin: true, expectedRevision: 0, now, ct);
        }

        await WriteUserFieldAsync(lease, workId, WorkFields.Name, title, ct);
        // Empty on creation means unknown. Clearing an existing year below is
        // an explicit user value, so those two gestures retain different intent.
        if (draft.FirstReleaseYear is not null)
        {
            await WriteUserFieldAsync(lease, workId, WorkFields.FirstReleaseYear,
                draft.FirstReleaseYear.Value.ToString(CultureInfo.InvariantCulture), ct);
        }

        var entry = await ReadAsync(lease, ownershipId, ct);
        batch.Commit();
        return entry!;
    }

    public async Task<ManualEntry?> GetAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await ReadAsync(lease, ownershipId, ct);
    }

    public async Task<IReadOnlyList<ManualEntry>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ManualEntry>(new CommandDefinition(
            $"SELECT {Columns} {From} ORDER BY w.name,m.ownership_id;",
            transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> UpdateAsync(long ownershipId, ManualGameDraft draft, CancellationToken ct = default)
    {
        draft = Validate(draft);
        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;
        var entry = await ReadAsync(lease, ownershipId, ct);
        if (entry is null)
        {
            return false;
        }

        if (draft.ExpectedIgdbMappingRevision is { } revision && revision != entry.IgdbMappingRevision)
        {
            throw ManualIdentifierWrites.Conflict(ExternalIdProviders.Igdb, ManualEntryConflictReason.MappingChanged);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        await AssertIdentifiersAreFreeAsync(lease, draft, entry.WorkId, ct);
        if (draft.SteamAppId != entry.SteamAppId)
        {
            await ManualIdentifierWrites.ReplaceAsync(lease, ownershipId, entry.ReleaseId, entry.WorkId,
                ExternalIdProviders.Steam, draft.SteamAppId, now, ct);
        }

        var changesIdentity = draft.IgdbId != entry.IgdbId;
        if (changesIdentity)
        {
            await WorkIgdbMappingWrites.TransitionAsync(lease, entry.WorkId, draft.IgdbId,
                pin: draft.IgdbId is not null, entry.IgdbMappingRevision, now, ct);
        }

        var title = draft.TrimmedTitle;
        if (changesIdentity || title != entry.Title)
        {
            await WriteUserFieldAsync(lease, entry.WorkId, WorkFields.Name, title, ct);
        }

        if (changesIdentity || draft.FirstReleaseYear != entry.FirstReleaseYear)
        {
            await WriteUserFieldAsync(lease, entry.WorkId, WorkFields.FirstReleaseYear,
                draft.FirstReleaseYear?.ToString(CultureInfo.InvariantCulture), ct);
        }

        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE works SET sort_name = @title WHERE id = @workId;
            UPDATE releases SET name = @title, platform = @platform
            WHERE id = @releaseId
              AND NOT EXISTS(SELECT 1 FROM ownerships WHERE release_id = @releaseId AND store <> 'manual');
            UPDATE ownerships SET install_path = @installPath, installed = @installed
            WHERE id = @ownershipId;
            UPDATE manual_entries
            SET executable_path = @executablePath, platform_label = @platform, updated_at = @now
            WHERE ownership_id = @ownershipId;
            """, new
            {
                title, workId = entry.WorkId, releaseId = entry.ReleaseId, ownershipId,
                platform = Trimmed(draft.PlatformLabel),
                installPath = draft.EffectiveInstallPath,
                installed = draft.IsInstalled,
                executablePath = Trimmed(draft.ExecutablePath),
                now,
            }, lease.Transaction, cancellationToken: ct));

        batch.Commit();
        return true;
    }

    public async Task<bool> DeleteAsync(long ownershipId, CancellationToken ct = default)
    {
        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;
        var entry = await ReadAsync(lease, ownershipId, ct);
        if (entry is null)
        {
            return false;
        }

        // Independently attached ownerships retain their release and history.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM ownerships WHERE id = @ownershipId;
            DELETE FROM releases WHERE id = @releaseId
              AND NOT EXISTS(SELECT 1 FROM ownerships WHERE release_id = @releaseId);
            DELETE FROM works WHERE id = @workId
              AND NOT EXISTS(SELECT 1 FROM releases WHERE work_id = @workId);
            """, new { ownershipId, releaseId = entry.ReleaseId, workId = entry.WorkId },
            lease.Transaction, cancellationToken: ct));
        batch.Commit();
        return true;
    }

    private static Task<ManualEntry?> ReadAsync(DbLease lease, long ownershipId, CancellationToken ct)
        => lease.Connection.QuerySingleOrDefaultAsync<ManualEntry>(new CommandDefinition(
            $"SELECT {Columns} {From} WHERE m.ownership_id = @ownershipId;",
            new { ownershipId }, lease.Transaction, cancellationToken: ct));

    private async Task WriteUserFieldAsync(DbLease lease, long workId, string field, string? value, CancellationToken ct)
    {
        var result = await _fields.SetFieldAsync(lease, workId, field, value, ct);
        if (result != WorkFieldEditOutcome.Applied)
        {
            throw new ArgumentException($"The manual metadata field '{field}' is invalid.", field);
        }
    }

    private static async Task AssertIdentifiersAreFreeAsync(
        DbLease lease, ManualGameDraft draft, long? workId, CancellationToken ct)
    {
        await ManualIdentifierWrites.AssertAvailableAsync(lease, workId, ExternalIdProviders.Igdb,
            draft.IgdbId?.ToString(CultureInfo.InvariantCulture), ct);
        await ManualIdentifierWrites.AssertAvailableAsync(lease, workId, ExternalIdProviders.Steam, draft.SteamAppId, ct);
    }

    private static ManualGameDraft Validate(ManualGameDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ArgumentException("A hand-added game needs a title.", nameof(ManualGameDraft.Title));
        }

        if (draft.FirstReleaseYear is { } year && !WorkFields.IsValidReleaseYear(year))
        {
            throw new ArgumentException("The release year is outside the supported range.", nameof(ManualGameDraft.FirstReleaseYear));
        }

        if (draft.IgdbId is <= 0)
        {
            throw new ArgumentException("An IGDB id must be positive.", nameof(ManualGameDraft.IgdbId));
        }

        var steamId = Trimmed(draft.SteamAppId);
        if (steamId is not null)
        {
            if (!uint.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            {
                throw new ArgumentException("A Steam appid must be a positive integer.", nameof(ManualGameDraft.SteamAppId));
            }

            steamId = parsed.ToString(CultureInfo.InvariantCulture);
        }

        return draft with { SteamAppId = steamId };
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
