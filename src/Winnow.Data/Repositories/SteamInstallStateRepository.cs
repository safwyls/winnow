using Dapper;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class SteamInstallStateRepository(ISqliteConnectionFactory factory) : ISteamInstallStateRepository
{
    public async Task ClearMissingAsync(IReadOnlyCollection<string> presentAppIds, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ownerships
            SET installed = 0, install_path = NULL
            WHERE store = 'steam'
              AND (installed <> 0 OR install_path IS NOT NULL)
              AND EXISTS (
                  SELECT 1 FROM external_ids
                  WHERE external_ids.release_id = ownerships.release_id
                    AND provider = 'steam')
              AND NOT EXISTS (
                  SELECT 1 FROM external_ids
                  WHERE external_ids.release_id = ownerships.release_id
                    AND provider = 'steam'
                    AND provider_id IN @presentAppIds);
            """, new { presentAppIds }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
