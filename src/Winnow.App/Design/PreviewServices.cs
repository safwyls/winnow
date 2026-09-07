using Winnow.App.Services;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.Design;

/// <summary>
/// The previewer's feed (TASK-150): two shelves over the fabricated library,
/// so the landing screen draws its cards. The ownership ids are
/// <see cref="PreviewLibrary"/>'s, which is how the cards resolve to real
/// tiles through <see cref="IGameTileSource"/>.
/// </summary>
internal sealed class PreviewFeedService : IFeedService
{
    public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
        => Task.FromResult(new FeedSnapshot(
            [
                new FeedShelf(
                    "patched",
                    "Patched while you were away",
                    "Something changed since the last time either of you looked.",
                    [
                        new FeedItem(
                            PreviewLibrary.StardewOwnership,
                            PreviewLibrary.StardewRelease,
                            "Stardew Valley",
                            "Patched 2 months ago, nine months after you last played"),
                        new FeedItem(
                            PreviewLibrary.HollowKnightOwnership,
                            PreviewLibrary.HollowKnightRelease,
                            "Hollow Knight",
                            "Silksong is out now"),
                    ]),
                new FeedShelf(
                    "untouched",
                    "Never opened",
                    "Owned, installed or otherwise, and never once launched.",
                    [
                        new FeedItem(
                            PreviewLibrary.DiscoElysiumOwnership,
                            PreviewLibrary.DiscoElysiumRelease,
                            "Disco Elysium",
                            "In the library for over a year, never played"),
                        new FeedItem(
                            PreviewLibrary.SlayTheSpireOwnership,
                            PreviewLibrary.SlayTheSpireRelease,
                            "Slay the Spire",
                            "Came with a bundle, never opened"),
                    ]),
            ],
            CandidateCount: 8,
            FeedConfidence.Settling,
            Failed: false));

    public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<FeedVerdictOutcome> RecordVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => Task.FromResult(new FeedVerdictOutcome(Saved: true, ExpiresAt: null));

    public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
}

/// <summary>
/// The previewer's store connections: nothing is connected and every write is
/// a safe no-op, so the Platforms panel draws its honest not-connected state
/// rather than an exception.
/// </summary>
internal sealed class PreviewStoreConnections : IStoreConnections
{
    public ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default)
        => ValueTask.FromResult(false);

    public ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default)
        => ValueTask.FromResult(SteamConnection.None);

    public Task<SteamApiKeySaveOutcome> SaveSteamApiKeyAsync(string? key, CancellationToken ct = default)
        => Task.FromResult(SteamApiKeySaveOutcome.Refused);

    public Task ClearSteamApiKeyAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult<StoreSession?>(null);

    public Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default)
        => Task.FromResult(new StoreSignInOutcome(
            Succeeded: false,
            DisplayName: null,
            Persisted: false,
            StoreSignInProblem.Cancelled,
            StoreSignInMessages.Cancelled));

    public Task SignOutOfEpicAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
