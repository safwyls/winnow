namespace Winnow.Plugin.Xbox;

/// <summary>Installation evidence for this Windows user, never proof of a purchased licence.</summary>
public sealed record XboxLocalGame(string PackageFamilyName, string Title)
{
    /// <summary>The title is the package identity because no display name could be resolved.</summary>
    public bool IsTitleProvisional { get; init; }
    public string? StoreId { get; init; }
    public string? TitleId { get; init; }
    public string? InstallPath { get; init; }
    public string? AppUserModelId { get; init; }
    public string? ExecutablePath { get; init; }
}

/// <summary>Only a complete scan can establish absence. Packages includes unclassified Store apps for exact remote game joins.</summary>
public sealed record XboxLocalScan(IReadOnlyList<XboxLocalGame> Games, bool IsComplete)
{
    public IReadOnlyList<XboxLocalGame> Packages { get; init; } = Games;
}

public interface IXboxLocalLibrary
{
    Task<XboxLocalScan?> ScanAsync(CancellationToken cancellationToken = default);
    Task<bool> LaunchAsync(string packageFamilyName, string applicationId, CancellationToken cancellationToken = default);
    Task<bool> OpenStoreAsync(string productId, CancellationToken cancellationToken = default);
}
