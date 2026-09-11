namespace Winnow.Core.Repositories;

public sealed record GogRegistryInstallation(string ProviderId, string? InstallPath);

/// <summary>Tracks registry provenance independently of durable ownership and play history.</summary>
public interface IGogInstallStateRepository
{
    /// <summary>
    /// Records positive registry evidence and refreshes install facts. Only a complete
    /// inventory clears previously observed registry installations, except products with
    /// a fresh positive Galaxy observation. Returns identifiers proved absent this pass.
    /// </summary>
    Task<IReadOnlyList<string>> ReconcileAsync(
        IReadOnlyList<string> observedRegistryIds,
        IReadOnlyList<GogRegistryInstallation> current,
        bool isComplete,
        IReadOnlyList<string> galaxyInstalledIds,
        CancellationToken ct = default);
}
