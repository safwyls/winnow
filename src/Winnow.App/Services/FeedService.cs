using System.Diagnostics;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Recommend;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// The only place in the app where the Feed screen's question meets the module
/// that can answer it, and the only place the feedback loop is written.
///
/// <para><b>Three jobs, and the second one is the load-bearing one.</b> It
/// translates <see cref="ShelfFeed"/> into the App-layer shapes in
/// <see cref="IFeedService"/> — see there for why the view model may not name a
/// scoring type — it gets the scoring pass off the UI thread, and it owns the
/// storage side of §6b: verdicts in, surfacings out.</para>
///
/// <para><b>Why <see cref="Task.Run(Func{Task})"/> around an async method.</b>
/// <c>RecommendationEngine</c> is asynchronous in signature and synchronous in
/// fact: every read underneath it is Dapper over Microsoft.Data.Sqlite, which
/// completes on the calling thread. Awaiting <c>GetShelvesAsync</c> from the
/// dispatcher would therefore run all ~500ms of it ON the dispatcher and freeze
/// the window — the await would never yield, because there is nothing to yield
/// to. Measured on the author's library: 638ms cold, 490–530ms warm, over 997
/// candidates. §5.1 pitfall 3 is exactly this. <b>Every method on this class
/// obeys the same rule</b>, including the three-row verdict writes: they are the
/// same synchronous provider, and a lock contended by a running scoring pass is
/// not a fast write.</para>
///
/// <para><b>The engine is optional and a missing one is an answer.</b> A host
/// that never registered it gets <see cref="FeedSnapshot.Unavailable"/>, which
/// the screen already draws as a sentence — an unregistered module degrades into
/// copy rather than into a startup failure, the same rule
/// <see cref="StoreConnections"/> follows. The feedback store is optional in
/// exactly the same way, and degrades further: with no store the feed still
/// computes, and only the loop is missing.</para>
///
/// <para><b>The §6b contract, in order, once per load.</b>
/// <c>FeedbackSets.LoadAsync</c> → <c>Apply(request)</c> →
/// <c>GetShelvesAsync</c> → <c>RecordSurfacedAsync(SurfacingsOf(feed, now))</c>.
/// All four run on the same background task and against the same instant, which
/// is what stops "active", "recent" and "today" disagreeing between the read
/// that feeds the engine and the write that records what it produced.</para>
///
/// <para><b>A failed surfacing write is not a failed feed.</b> The log is the
/// engine's cross-day rotation memory (§6a), so losing a day of it costs
/// rotation quality — the user sees some of yesterday's picks again — and
/// nothing else. It is therefore caught and logged where it happens, inside the
/// background task, and the computed feed is returned regardless. Letting it
/// throw would turn "rotation is slightly worse today" into "the Feed screen is
/// an apology", which is the wrong trade by an enormous margin.</para>
/// </summary>
public sealed class FeedService : IFeedService
{
    /// <summary>
    /// The model's parameters, resolved once so that the request and the
    /// feedback read cannot drift. <c>FeedbackSets.LoadAsync</c> reads
    /// <c>SurfacedWindowDays</c> and <c>EndorsementWindowDays</c> off it while
    /// the engine reads every other number, and two instances here would mean a
    /// recently-surfaced set computed over a different window than the penalty
    /// that consumes it.
    /// </summary>
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;

    private readonly IRecommendationEngine? _engine;
    private readonly IFeedFeedbackRepository? _feedback;
    private readonly TimeProvider _clock;
    private readonly ILogger<FeedService>? _log;

    public FeedService(
        IRecommendationEngine? engine = null,
        IFeedFeedbackRepository? feedback = null,
        TimeProvider? clock = null,
        ILogger<FeedService>? log = null)
    {
        _engine = engine;
        _feedback = feedback;
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

        // One instant for the whole pass. The engine derives its shuffle seed
        // from this DATE, so the feed rotates daily and is stable within a day —
        // refreshing the screen must not deal a new hand — and the feedback read
        // and the surfacing write are stamped with the same day for the same
        // reason.
        var now = _clock.GetUtcNow().UtcDateTime;

        try
        {
            var started = Stopwatch.GetTimestamp();

            // See the class remarks: the awaits inside are over a synchronous
            // provider and never yield, so this Task.Run is the whole of what
            // keeps the window responsive. The feedback read is inside it for
            // exactly the same reason the scoring pass is.
            var feed = await Task.Run(() => ComputeAsync(now, ct), ct).ConfigureAwait(false);

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

    /// <inheritdoc/>
    public async Task<FeedVerdictOutcome> RecordVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
    {
        if (_feedback is null)
        {
            return FeedVerdictOutcome.NotSaved;
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        // The schema's pairing, and the service is where it is honoured: a
        // snooze MUST carry an expiry (0011's CHECK forbids omitting it) and a
        // dismissal MUST NOT (the same CHECK forbids supplying one). Deciding it
        // here rather than at the button is what lets the card say "back on the
        // 26th" without knowing how long "not now" is.
        var expiresAt = kind == FeedVerdictKind.Snoozed
            ? now + FeedVerdictKinds.DefaultSnooze
            : (DateTime?)null;

        try
        {
            await Task.Run(
                () => _feedback.RecordVerdictAsync(
                    new FeedVerdict
                    {
                        ReleaseId = releaseId,
                        Kind = StorageKind(kind),
                        CreatedAt = now,
                        ExpiresAt = expiresAt,
                    },
                    ct),
                ct).ConfigureAwait(false);

            return new FeedVerdictOutcome(Saved: true, expiresAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reported rather than thrown, because the card has to be able to
            // tell the truth about it. A receipt over a write that did not land
            // is the one lie this surface cannot afford.
            _log?.LogWarning(ex, "Could not store a {Kind} verdict for release {ReleaseId}.", kind, releaseId);
            return FeedVerdictOutcome.NotSaved;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
    {
        if (_feedback is null)
        {
            return false;
        }

        try
        {
            var revoked = await Task.Run(
                () => _feedback.RevokeVerdictsAsync(
                    releaseId, StorageKind(kind), _clock.GetUtcNow().UtcDateTime, ct),
                ct).ConfigureAwait(false);

            return revoked > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Could not revoke the {Kind} verdict on release {ReleaseId}.", kind, releaseId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
    {
        if (_feedback is null)
        {
            return [];
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        try
        {
            var rows = await Task.Run(() => _feedback.GetAllVerdictsAsync(ct), ct).ConfigureAwait(false);

            var records = new List<FeedVerdictRecord>(rows.Count);
            foreach (var row in rows)
            {
                // A kind this build does not know about is dropped rather than
                // guessed at — the same rule FeedbackSets applies on the read
                // side. A verdict must never silently mean something else, and
                // an inspection screen is the worst place for it to.
                if (Kind(row.Kind) is not { } kind)
                {
                    continue;
                }

                records.Add(new FeedVerdictRecord(
                    row.ReleaseId, kind, row.CreatedAt, row.ExpiresAt, row.RevokedAt, Status(row, now)));
            }

            return records;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Could not read the feedback history.");
            return [];
        }
    }

    /// <summary>
    /// §6b's five steps, minus the two the user's clicks drive: read the
    /// feedback sets, stamp them on the request, score, log what was shown.
    /// Runs entirely on a background thread — see the class remarks.
    /// </summary>
    private async Task<ShelfFeed> ComputeAsync(DateTime now, CancellationToken ct)
    {
        // No store is a real state, not an error: the feed still computes, it
        // just has no memory of what it said or what was said to it.
        var sets = _feedback is null
            ? FeedbackSets.Empty
            : await FeedbackSets.LoadAsync(_feedback, now, Tuning, ct).ConfigureAwait(false);

        var request = sets.Apply(new RecommendationRequest
        {
            AsOfUtc = now,
            Tuning = Tuning,

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
        });

        var feed = await _engine!.GetShelvesAsync(request, ct).ConfigureAwait(false);

        await RecordSurfacedAsync(feed, now, ct).ConfigureAwait(false);

        return feed;
    }

    /// <summary>
    /// Appends what this feed showed to the surfacing log — the cross-day
    /// memory behind rotation (§6a) and half of the launch-endorsement join
    /// (§6b). Idempotent per (release, day), so a same-day refresh writes
    /// nothing and cannot shift the day's feed.
    ///
    /// <para><b>It swallows.</b> This is a write on a path that used to only
    /// read, and the feed must survive it failing: the cost of a lost day is
    /// that tomorrow's rotation repeats some of today's picks, which is a worse
    /// feed and not a broken one. The alternative — an exception out of here —
    /// would replace five shelves of real recommendations with an apology over a
    /// bookkeeping row.</para>
    /// </summary>
    private async Task RecordSurfacedAsync(ShelfFeed feed, DateTime now, CancellationToken ct)
    {
        if (_feedback is null)
        {
            return;
        }

        var surfacings = FeedbackSets.SurfacingsOf(feed, now);
        if (surfacings.Count == 0)
        {
            return;
        }

        try
        {
            await _feedback.RecordSurfacedAsync(surfacings, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The pass was superseded or the window closed. The caller's own
            // catch turns this into "nothing happened", which is right.
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(
                ex,
                "Could not log {Count} surfacings; the feed is unaffected and tomorrow's rotation is weaker.",
                surfacings.Count);
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

    /// <summary>The App layer's word for the kind, in the schema's own vocabulary. The seam, in two lines.</summary>
    private static string StorageKind(FeedVerdictKind kind) => kind switch
    {
        FeedVerdictKind.Snoozed => FeedVerdictKinds.Snoozed,
        _ => FeedVerdictKinds.NotInterested,
    };

    /// <summary>The reverse, and null for a kind this build has no word for.</summary>
    private static FeedVerdictKind? Kind(string storageKind) => storageKind switch
    {
        FeedVerdictKinds.NotInterested => FeedVerdictKind.NotInterested,
        FeedVerdictKinds.Snoozed => FeedVerdictKind.Snoozed,
        _ => null,
    };

    /// <summary>
    /// Where a stored row stands, computed rather than read: "active" is never a
    /// column (§6b), so a lapsed snooze becomes inactive with no write anywhere
    /// and there is no cached state to drift.
    /// </summary>
    private static FeedVerdictStatus Status(FeedVerdict verdict, DateTime now)
    {
        if (verdict.RevokedAt is not null)
        {
            return FeedVerdictStatus.Undone;
        }

        return verdict.IsActiveAt(now) ? FeedVerdictStatus.Active : FeedVerdictStatus.Lapsed;
    }
}
