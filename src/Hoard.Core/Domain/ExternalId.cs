namespace Hoard.Core.Domain;

/// <summary>
/// A provider-scoped identifier for a <see cref="Release"/>
/// (Steam appid, GOG id, Epic catalog id, IGDB id).
/// Provider values come from <see cref="ExternalIdProviders"/>.
/// </summary>
public sealed record ExternalId
{
    public required long ReleaseId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderId { get; init; }
}
