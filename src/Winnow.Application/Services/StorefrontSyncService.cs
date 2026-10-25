using Dapper;
using Winnow.Data;
using Winnow.Enrich.Stores;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>Warms storefront responses behind first paint. The UI reads StorefrontCache only.</summary>
public sealed class StorefrontSyncService(ISqliteConnectionFactory factory, StorefrontClient client,
    ILogger<StorefrontSyncService> logger, IEpicLaunchKeys? epicLaunchKeys = null)
{
    public async Task SyncAsync(CancellationToken ct = default)
    {
        try { await SyncCoreAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Storefront refresh failed ({Failure}); existing metadata remains available.", ex.GetType().Name);
        }
    }

    private async Task SyncCoreAsync(CancellationToken ct)
    {
        Target[] targets;
        using (var lease = factory.Lease())
        {
            targets = (await lease.Connection.QueryAsync<Target>(new CommandDefinition("""
                SELECT DISTINCT e.provider AS Provider, e.provider_id AS Id
                FROM external_ids e JOIN ownerships o ON o.release_id = e.release_id
                WHERE e.provider IN ('epic', 'gog')
                """, transaction: lease.Transaction, cancellationToken: ct))).ToArray();
        }
        if (targets.Any(t => t.Provider == "epic"))
        {
            if (epicLaunchKeys is null) await client.RefreshEpicAsync(ct);
            else
            {
                var keys = await epicLaunchKeys.GetAllAsync(ct);
                var namespaces = targets.Where(t => t.Provider == "epic" && keys.ContainsKey(t.Id))
                    .Select(t => keys[t.Id].Namespace);
                await client.RefreshEpicNamespacesAsync(namespaces, ct);
            }
        }
        foreach (var target in targets.Where(t => t.Provider == "gog")) await client.RefreshGogAsync(target.Id, ct);
    }

    private sealed record Target(string Provider, string Id);
}
