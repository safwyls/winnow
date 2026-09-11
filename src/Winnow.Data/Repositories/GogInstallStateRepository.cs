using Dapper;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class GogInstallStateRepository(ISqliteConnectionFactory factory) : IGogInstallStateRepository
{
    public async Task<IReadOnlyList<string>> ReconcileAsync(
        IReadOnlyList<string> observedRegistryIds, IReadOnlyList<GogRegistryInstallation> current,
        bool isComplete, IReadOnlyList<string> galaxyInstalledIds, CancellationToken ct = default)
    {
        using var batch = new RepositoryWriteBatch(factory);
        var lease = batch.Lease;
        var transaction = lease.Transaction;
        var observed = observedRegistryIds.Concat(current.Select(game => game.ProviderId))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var providerId in observed)
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO gog_registry_installations(provider_id) VALUES (@providerId)
                ON CONFLICT DO NOTHING;
                """, new { providerId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var game in current)
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ownerships SET installed=1, install_path=COALESCE(@InstallPath, install_path)
                WHERE store='gog' AND release_id IN (
                    SELECT release_id FROM external_ids WHERE provider='gog' AND provider_id=@ProviderId);
                """, game, transaction, cancellationToken: ct)).ConfigureAwait(false);

        IReadOnlyList<string> absent = [];
        if (isComplete)
        {
            var present = current.Select(game => game.ProviderId).Concat(galaxyInstalledIds)
                .Distinct(StringComparer.Ordinal).ToArray();
            absent = (await lease.Connection.QueryAsync<string>(new CommandDefinition("""
                SELECT provider_id FROM gog_registry_installations WHERE provider_id NOT IN @present;
                """, new { present }, transaction, cancellationToken: ct)).ConfigureAwait(false)).AsList();
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                UPDATE ownerships SET installed=0, install_path=NULL
                WHERE store='gog' AND release_id IN (
                    SELECT release_id FROM external_ids WHERE provider='gog' AND provider_id IN @absent);
                """, new { absent }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        }
        batch.Commit();
        return absent;
    }
}
