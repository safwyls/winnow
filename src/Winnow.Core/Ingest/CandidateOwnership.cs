namespace Winnow.Core.Ingest;

/// <summary>
/// The normalised record every <c>Ingest.*</c> module emits (§5.1). The resolver
/// maps candidates onto Work/Release/Ownership; ingest must never write those directly.
/// </summary>
/// <param name="Provider">Source platform key (e.g. "steam") — see <see cref="Domain.ExternalIdProviders"/>.</param>
/// <param name="ProviderId">Platform-native id (Steam appid, GOG product id, Epic catalog item id).</param>
/// <param name="Title">Raw title. Null means "known to exist but unnamed" (provisional).</param>
/// <param name="AccountRef">Opaque source account reference (e.g. Steam3 id), if known.</param>
/// <param name="InstallPath">Local install directory. Meaningful only when <paramref name="Installed"/> is non-null.</param>
/// <param name="Installed">Three-valued: true/false when source inspected disk, null when it cannot know.</param>
/// <param name="PlaytimeMinutes">Cumulative playtime, if the source exposes it.</param>
/// <param name="LastPlayedAt">Last-played timestamp (UTC), if the source exposes it.</param>
/// <param name="AcquiredAt">Acquisition timestamp (UTC), if the source exposes it.</param>
/// <param name="Source">Which reader produced this (e.g. "steam_local") -- kept for provenance.</param>
/// <param name="ObservedAt">When the reader observed this state (UTC).</param>
public sealed record CandidateOwnership(
    string Provider,
    string ProviderId,
    string? Title,
    string? AccountRef,
    string? InstallPath,
    bool? Installed,
    long? PlaytimeMinutes,
    DateTime? LastPlayedAt,
    DateTime? AcquiredAt,
    string Source,
    DateTime ObservedAt);
