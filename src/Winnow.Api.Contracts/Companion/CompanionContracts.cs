namespace Winnow.Api.Contracts.Companion;

// What a paired phone reads over the LAN listener. Version 1 is additive-only: new
// fields may appear, and phones ignore fields they do not know. A change that removes or
// reinterprets a field is version 2 on a new route.

/// <summary>The read-only library a paired phone receives.</summary>
public sealed record CompanionSnapshot(int Version, DateTime GeneratedAt, IReadOnlyList<CompanionGame> Games,
    IReadOnlyList<CompanionList> Lists)
{
    public const int CurrentVersion = 1;
}

/// <summary>One game as Winnow resolves it: linked works count as one.</summary>
public sealed record CompanionGame(long WorkId, string Title, long? IgdbId, int? FirstReleaseYear, string? Summary,
    string? CoverUrl, string Bucket, long PlaytimeMinutes, DateTime? LastPlayedAt, IReadOnlyList<CompanionEntry> Entries);

/// <summary>One owned copy. <see cref="StoreIds"/> maps a provider (steam, gog, epic) to its
/// ID for this release. Account identifiers, install paths and prices are left out.</summary>
public sealed record CompanionEntry(long ReleaseId, string Store, string Title, string? Edition, long? IgdbVersionId,
    string? Platform, IReadOnlyDictionary<string, string> StoreIds, long PlaytimeMinutes, DateTime? LastPlayedAt,
    DateTime? AcquiredAt);

/// <summary>A manual list in the user's order. Live lists are left out: their filters
/// run on Winnow's facets, which phones do not have.</summary>
public sealed record CompanionList(long Id, string Name, IReadOnlyList<long> ReleaseIds);

/// <summary>Sent once, with the single-use code from the QR code.</summary>
public sealed record CompanionPairRequest(string Code, string DeviceName);

/// <summary>The device token is shown once; Winnow keeps only its hash.</summary>
public sealed record CompanionPairResponse(string DeviceId, string Token, string ComputerName);

// Loopback API records, for the desktop and fullscreen settings.

public sealed record CompanionStatus(bool Enabled, bool Running, int Port, string? Problem, string? Fingerprint,
    IReadOnlyList<string> Addresses, IReadOnlyList<CompanionDevice> Devices, CompanionPairing? Pairing);

public sealed record CompanionDevice(string Id, string Name, DateTime PairedAt, DateTime? LastSyncAt);

/// <summary>An open pairing window. <see cref="QrPayload"/> carries the addresses, port,
/// certificate fingerprint and code a phone needs.</summary>
public sealed record CompanionPairing(string Code, DateTime ExpiresAt, string QrPayload);

public sealed record SetCompanionEnabled(bool Enabled);
