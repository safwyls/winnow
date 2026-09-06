namespace Winnow.Core.Repositories;

public interface ISteamInstallStateRepository
{
    /// <summary>
    /// Clears Steam install flags and paths absent from a complete manifest scan.
    /// Ownership and play history remain intact, including games never launched.
    /// </summary>
    Task ClearMissingAsync(IReadOnlyCollection<string> presentAppIds, CancellationToken ct = default);
}
