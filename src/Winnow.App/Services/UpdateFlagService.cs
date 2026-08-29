using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>
/// Writes <see cref="UpdateAcknowledgement"/> records and computes the
/// "major update" watermark. Gets writes off the UI thread; degrades to
/// <see cref="UpdateFlagOutcome.NotStored"/> when no repository is registered.
/// </summary>
public sealed class UpdateFlagService : IUpdateFlagService
{
    private readonly IUpdateAcknowledgementRepository? _acknowledgements;
    private readonly BucketThresholds _thresholds;
    private readonly TimeProvider _clock;
    private readonly ILogger<UpdateFlagService>? _log;

    /// <param name="thresholds">
    /// The same parameter object the bucket query is called with, because
    /// <see cref="BucketThresholds.UpdateCorrelationWindowDays"/> has to be the
    /// same number on both sides: a watermark computed under a 7-day window and
    /// compared under a 14-day one is a dismissal that acknowledges a different
    /// push than the one the user was looking at. Defaults to
    /// <see cref="BucketThresholds.Default"/>, which is what
    /// <c>LibraryViewModel</c> loads with.
    /// </param>
    public UpdateFlagService(
        IUpdateAcknowledgementRepository? acknowledgements = null,
        BucketThresholds? thresholds = null,
        TimeProvider? clock = null,
        ILogger<UpdateFlagService>? log = null)
    {
        _acknowledgements = acknowledgements;
        _thresholds = thresholds ?? BucketThresholds.Default;
        _clock = clock ?? TimeProvider.System;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task<UpdateFlagOutcome> DismissAsync(
        long releaseId, IReadOnlyList<UpdateEvent> events, CancellationToken ct = default)
    {
        if (_acknowledgements is null)
        {
            return UpdateFlagOutcome.NotStored;
        }

        if (FlaggingPushAt(releaseId, events, _thresholds.UpdateCorrelationWindowDays) is not { } through)
        {
            // No correlated push, so no instant to acknowledge. Never fall back
            // to the clock: see IUpdateFlagService for what that would swallow.
            // The panel cannot reach this — it offers the control only on a
            // release the bucket query flagged — so it is reported rather than
            // papered over, because the only way to reach it is a bug worth
            // seeing.
            _log?.LogWarning(
                "Asked to dismiss the update flag on release {ReleaseId}, which has no correlated major update.",
                releaseId);
            return UpdateFlagOutcome.NothingToDo;
        }

        try
        {
            var ack = new UpdateAcknowledgement
            {
                ReleaseId = releaseId,
                AcknowledgedThrough = through,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            };

            await Task.Run(() => _acknowledgements.RecordAsync(ack, ct), ct).ConfigureAwait(false);

            return new UpdateFlagOutcome(UpdateFlagResult.Stored, through);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reported rather than thrown, so the control can tell the truth
            // about it. A dot that goes out over a write that did not land is
            // back tomorrow in front of a user who already answered for it.
            _log?.LogWarning(ex, "Could not store an update acknowledgement for release {ReleaseId}.", releaseId);
            return UpdateFlagOutcome.NotStored;
        }
    }

    /// <inheritdoc/>
    public async Task<UpdateFlagOutcome> RestoreAsync(long releaseId, CancellationToken ct = default)
    {
        if (_acknowledgements is null)
        {
            return UpdateFlagOutcome.NotStored;
        }

        try
        {
            var revoked = await Task.Run(
                () => _acknowledgements.RevokeAsync(releaseId, _clock.GetUtcNow().UtcDateTime, ct),
                ct).ConfigureAwait(false);

            // Zero rows is not a failure. A newer correlated push may have
            // outranked the watermark between the panel opening and the click,
            // in which case there was nothing left to take back and the user is
            // already looking at the flag they asked for.
            return revoked > 0 ? new UpdateFlagOutcome(UpdateFlagResult.Stored, null) : UpdateFlagOutcome.NothingToDo;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Could not revoke the update acknowledgements on release {ReleaseId}.", releaseId);
            return UpdateFlagOutcome.NotStored;
        }
    }

    /// <inheritdoc/>
    public async Task<DateTime?> GetStandingAsync(long releaseId, CancellationToken ct = default)
    {
        if (_acknowledgements is null)
        {
            return null;
        }

        try
        {
            var standing = await Task.Run(
                () => _acknowledgements.GetStandingAsync(releaseId, ct), ct).ConfigureAwait(false);

            return standing is null ? null : AsUtc(standing.AcknowledgedThrough);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Null costs the undo and leaves every row lit. That is the
            // direction to fail in — the alternative is hiding unread marks
            // because a read hiccuped.
            _log?.LogWarning(ex, "Could not read the standing update acknowledgement for release {ReleaseId}.", releaseId);
            return null;
        }
    }

    /// <inheritdoc/>
    public DateTime? ReadThrough(
        long releaseId, IReadOnlyList<UpdateEvent> events, DateTime? acknowledgedThrough)
    {
        if (acknowledgedThrough is not { } watermark)
        {
            return null;
        }

        var through = AsUtc(watermark);
        var read = through;

        foreach (var news in events)
        {
            if (news.ReleaseId != releaseId || news.Kind != UpdateEventKinds.Announcement)
            {
                continue;
            }

            var newsAt = AsUtc(news.OccurredAt);

            // Measured from the WATERMARK, never from the running answer. Off
            // the running answer this would chain — a fortnightly announcement
            // series would walk the "read" line forward for ever and quietly
            // acknowledge updates the user has never seen.
            if (newsAt > read && Math.Abs((newsAt - through).TotalDays) <= _thresholds.UpdateCorrelationWindowDays)
            {
                read = newsAt;
            }
        }

        return read;
    }

    /// <summary>
    /// The instant a dismissal acknowledges: the newest build push on this
    /// release that a nearby announcement corroborates. Mirrors the
    /// <c>major_update</c> CTE in <c>LibraryQueryRepository</c>.
    /// </summary>
    internal static DateTime? FlaggingPushAt(
        long releaseId, IReadOnlyList<UpdateEvent> events, int correlationWindowDays)
    {
        DateTime? newest = null;

        foreach (var push in events)
        {
            // Scoped to the one release, as the CTE's join is. The caller reads
            // by release so this is normally a no-op; it is here because a
            // watermark derived from another release's push is a silent,
            // permanent error in the user's own data.
            if (push.ReleaseId != releaseId || push.Kind != UpdateEventKinds.BuildPush)
            {
                continue;
            }

            var pushAt = AsUtc(push.OccurredAt);

            if (newest is { } best && pushAt <= best)
            {
                continue;
            }

            if (HasCorrelatedAnnouncement(releaseId, events, pushAt, correlationWindowDays))
            {
                newest = pushAt;
            }
        }

        return newest;
    }

    /// <summary>
    /// The CTE's <c>EXISTS</c>, in C#. <c>julianday</c> differences are days
    /// including the fractional part, so <see cref="TimeSpan.TotalDays"/> is the
    /// same measure and the boundary is inclusive on both sides exactly as
    /// <c>&lt;=</c> makes it there.
    /// </summary>
    private static bool HasCorrelatedAnnouncement(
        long releaseId, IReadOnlyList<UpdateEvent> events, DateTime pushAt, int correlationWindowDays)
    {
        foreach (var news in events)
        {
            if (news.ReleaseId != releaseId || news.Kind != UpdateEventKinds.Announcement)
            {
                continue;
            }

            if (Math.Abs((AsUtc(news.OccurredAt) - pushAt).TotalDays) <= correlationWindowDays)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Timestamps come back from SQLite as TEXT and therefore as
    /// <see cref="DateTimeKind.Unspecified"/>. They are UTC by the storage
    /// contract, so say so before comparing — the same normalisation
    /// <c>UpdateEventViewModel.AsUtc</c> applies for the same reason, restated
    /// here rather than borrowed so a service does not reach into the view
    /// models for a rule about storage.
    /// </summary>
    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
