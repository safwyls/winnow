using System.Diagnostics;
using Hoard.Recommend;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// The only place in the app where the Feed screen's question meets the module
/// that can answer it.
///
/// <para><b>Two jobs, and the second one is the load-bearing one.</b> It
/// translates <see cref="ShelfFeed"/> into the App-layer shapes in
/// <see cref="IFeedService"/> — see there for why the view model may not name a
/// scoring type — and it gets the scoring pass off the UI thread.</para>
///
/// <para><b>Why <see cref="Task.Run(Func{Task})"/> around an async method.</b>
/// <c>RecommendationEngine</c> is asynchronous in signature and synchronous in
/// fact: every read underneath it is Dapper over Microsoft.Data.Sqlite, which
/// completes on the calling thread. Awaiting <c>GetShelvesAsync</c> from the
/// dispatcher would therefore run all ~500ms of it ON the dispatcher and freeze
/// the window — the await would never yield, because there is nothing to yield
/// to. Measured on the author's library: 638ms cold, 490–530ms warm, over 997
/// candidates. §5.1 pitfall 3 is exactly this.</para>
///
/// <para><b>The engine is optional and a missing one is an answer.</b> A host
/// that never registered it gets <see cref="FeedSnapshot.Unavailable"/>, which
/// the screen already draws as a sentence — an unregistered module degrades into
/// copy rather than into a startup failure, the same rule
/// <see cref="StoreConnections"/> follows.</para>
///
/// <para><b>Nothing persists.</b> The three id sets a request can carry —
/// dismissed, snoozed, already-shown — are the caller's bookkeeping, and M8
/// deliberately keeps none of them: there is no dismiss affordance yet, and the
/// engine's own day-seeded jitter is what rotates the feed. When a "not for me"
/// button lands, this is the class that grows the storage, and it is the only
/// one that has to.</para>
/// </summary>
public sealed class FeedService : IFeedService
{
    private readonly IRecommendationEngine? _engine;
    private readonly TimeProvider _clock;
    private readonly ILogger<FeedService>? _log;

    public FeedService(
        IRecommendationEngine? engine = null,
        TimeProvider? clock = null,
        ILogger<FeedService>? log = null)
    {
        _engine = engine;
        _clock = clock ?? TimeProvider.System;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
    {
        if (_engine is null)
        {
            return FeedSnapshot.Unavailable;
        }

        var request = new RecommendationRequest
        {
            // The engine derives its shuffle seed from this instant's DATE, so
            // the feed rotates daily and is stable within a day. Refreshing the
            // screen must not deal a new hand.
            AsOfUtc = _clock.GetUtcNow().UtcDateTime,

            // Six, and the number came from measuring the grid rather than from
            // taste. Ten was a RAIL's number: items past the right edge cost no
            // vertical space, so the cap was free. Sections wrap now, and every
            // item occupies real height — at the 1200px minimum a ten-item
            // section is five rows and the whole feed runs to roughly six
            // screenfuls. Six is three rows there and one or two above 1600.
            //
            // Past it a shelf stops being a pitch and becomes another list,
            // which is the surface the library already is, one click away.
            //
            // Coupled to RecommendationTuning.ShelfGenreCap, which moved 4 -> 3
            // in the same change: the property it defends is that no genre may
            // take a MAJORITY of a shelf, and 4 of 6 is two-thirds. Moving this
            // number alone would silently re-break the constant that exists to
            // prevent exactly that.
            MaxPerShelf = 6,
        };

        try
        {
            var started = Stopwatch.GetTimestamp();

            // See the class remarks: the await inside the engine never yields,
            // so this Task.Run is the whole of what keeps the window responsive.
            var feed = await Task.Run(() => _engine.GetShelvesAsync(request, ct), ct)
                .ConfigureAwait(false);

            _log?.LogInformation(
                "Feed scored {Candidates} candidates into {Shelves} shelves in {Elapsed:0} ms (tier {Tier}).",
                feed.CandidateCount,
                feed.Shelves.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                feed.Tier);

            return new FeedSnapshot(
                feed.Shelves.Select(Translate).ToList(),
                feed.CandidateCount,
                Confidence(feed.Tier),
                Failed: false);
        }
        catch (OperationCanceledException)
        {
            // The window closed, or a reload superseded this pass. Not a
            // failure to report — the caller that cancelled knows.
            throw;
        }
        catch (Exception ex)
        {
            // A recommendation is the one thing in this app that must never be
            // load-bearing for the app running (charter: derived, never truth).
            // The screen says so and offers the library; nothing else changes.
            _log?.LogWarning(ex, "Could not compute the feed; the library is unaffected.");
            return FeedSnapshot.Unavailable;
        }
    }

    private static FeedShelf Translate(RecommendationShelf shelf)
        => new(
            shelf.Id,
            shelf.Title,
            shelf.Blurb,
            shelf.Items
                .Select(i => new FeedItem(i.OwnershipId, i.ReleaseId, i.Title, i.Reason))
                .ToList());

    private static FeedConfidence Confidence(DataTier tier) => tier switch
    {
        DataTier.Established => FeedConfidence.Established,
        DataTier.Settling => FeedConfidence.Settling,
        _ => FeedConfidence.EarlyDays,
    };
}
