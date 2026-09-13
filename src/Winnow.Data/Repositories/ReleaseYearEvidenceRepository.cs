using Dapper;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class ReleaseYearEvidenceRepository(ISqliteConnectionFactory factory) : IReleaseYearEvidenceRepository
{
    public async Task<bool> ObserveSteamAsync(long releaseId, string appId, int year, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);
        using var lease = factory.Lease();
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO release_year_evidence (release_id, source, source_id, year)
            SELECT @releaseId, 'steam_original_release_date', @appId, @year
            WHERE EXISTS (
                SELECT 1 FROM external_ids
                WHERE release_id = @releaseId AND provider = 'steam' AND provider_id = @appId)
            ON CONFLICT (release_id, source, source_id) DO UPDATE SET year = excluded.year
            WHERE release_year_evidence.year <> excluded.year;
            """, new { releaseId, appId, year }, transaction: lease.Transaction, cancellationToken: ct)) > 0;
    }
}
