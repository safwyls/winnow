using Microsoft.Extensions.Time.Testing;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The UI half of "dismiss the Patched since flag until a genuinely newer
/// update arrives" (design-system.md §5.2, migration 0012).
///
/// <para>Three things are worth testing here and the storage tests cover none
/// of them. <b>What instant gets written</b> — the flagging build push's own
/// <c>occurred_at</c> and never the clock, because stamping the clock would
/// swallow a push that landed between the read that drew the badge and the
/// click that dismissed it. <b>What the panel says when the write did not
/// land</b> — nothing, because a receipt over a failed write is the one lie
/// this surface cannot tell. And <b>which rows keep the Flare dot</b>, because
/// §2 gives Flare one meaning and a row the user has explicitly declared read
/// is not unread.</para>
///
/// <para>These are pure service and view-model tests: no database, no Avalonia
/// application. The acknowledgement store is faked so that "the write threw" is
/// a state a test can actually produce.</para>
/// </summary>
public sealed class UpdateFlagTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private const long ReleaseId = 7;

    /// <summary>7 days, the same window the bucket query is run with.</summary>
    private static readonly int Window = BucketThresholds.Default.UpdateCorrelationWindowDays;

    // ══ The watermark ═══════════════════════════════════════════════════════

    /// <summary>
    /// The one assertion the whole feature rests on. The dismissal is a
    /// watermark on the release's update timeline, not a note of when the user
    /// clicked — so what is stored is the push's instant, and the clock appears
    /// only in <see cref="UpdateAcknowledgement.CreatedAt"/>, which nothing
    /// compares against anything.
    /// </summary>
    [Fact]
    public async Task The_watermark_is_the_flagging_push_and_never_the_clock()
    {
        var pushedAt = Now.AddDays(-11);
        var store = new FakeAcknowledgements();

        var outcome = await Service(store).DismissAsync(ReleaseId, Correlated(pushedAt));

        Assert.True(outcome.Saved);
        Assert.Equal(pushedAt, outcome.AcknowledgedThrough);

        var written = Assert.Single(store.Recorded);
        Assert.Equal(pushedAt, written.AcknowledgedThrough);
        Assert.Equal(ReleaseId, written.ReleaseId);

        // The click's own timestamp is recorded, separately, and is not the
        // watermark. If these two were ever the same value the feature would
        // silently acquire the race it exists without.
        Assert.Equal(Now, written.CreatedAt);
        Assert.NotEqual(written.CreatedAt, written.AcknowledgedThrough);
    }

    /// <summary>
    /// The newest correlated push wins, which is what makes one click
    /// acknowledge everything the user has just read rather than the oldest
    /// thing in the list.
    /// </summary>
    [Fact]
    public async Task The_newest_correlated_push_is_the_one_acknowledged()
    {
        var older = Now.AddDays(-200);
        var newer = Now.AddDays(-9);

        var events = new List<UpdateEvent>();
        events.AddRange(Correlated(older));
        events.AddRange(Correlated(newer));

        var store = new FakeAcknowledgements();
        var outcome = await Service(store).DismissAsync(ReleaseId, events);

        Assert.Equal(newer, outcome.AcknowledgedThrough);
    }

    /// <summary>
    /// A lone depot push is a DRM bump, a localization file or a one-line
    /// hotfix — §4.5's whole reason for requiring two signals. It never flagged
    /// the game, so it must not become the watermark either: doing so would
    /// acknowledge, sight unseen, the real patch that lands next week.
    /// </summary>
    [Fact]
    public async Task An_uncorrelated_later_push_does_not_move_the_watermark()
    {
        var flagging = Now.AddDays(-30);

        var events = new List<UpdateEvent>(Correlated(flagging))
        {
            // Twelve days after the correlated pair and eight days past the
            // 7-day window from the nearest announcement — noise, by the same
            // rule the CTE applies.
            Push(Now.AddDays(-18)),
        };

        var store = new FakeAcknowledgements();
        var outcome = await Service(store).DismissAsync(ReleaseId, events);

        Assert.Equal(flagging, outcome.AcknowledgedThrough);
        Assert.Equal(flagging, Assert.Single(store.Recorded).AcknowledgedThrough);
    }

    /// <summary>
    /// The announcement is the corroboration, not the timestamp. An
    /// announcement with no push behind it is marketing, and marketing does not
    /// change the user's install.
    /// </summary>
    [Fact]
    public async Task An_announcement_alone_is_nothing_to_dismiss()
    {
        var store = new FakeAcknowledgements();

        var outcome = await Service(store).DismissAsync(
            ReleaseId, [Announcement(Now.AddDays(-3))]);

        Assert.Equal(UpdateFlagResult.NothingToDo, outcome.Result);
        Assert.False(outcome.Saved);
        Assert.Null(outcome.AcknowledgedThrough);
        Assert.Empty(store.Recorded);
    }

    /// <summary>
    /// Another release's events are not this release's history. The caller
    /// reads by release so this is normally unreachable — it is asserted
    /// because a watermark from the wrong game is a silent, permanent error in
    /// the user's own data.
    /// </summary>
    [Fact]
    public async Task Another_releases_push_is_not_a_watermark_for_this_one()
    {
        var events = Correlated(Now.AddDays(-4)).Select(e => e with { ReleaseId = ReleaseId + 1 }).ToList();

        var outcome = await Service(new FakeAcknowledgements()).DismissAsync(ReleaseId, events);

        Assert.Equal(UpdateFlagResult.NothingToDo, outcome.Result);
    }

    // ══ Failure, and undo ═══════════════════════════════════════════════════

    [Fact]
    public async Task A_store_that_throws_is_reported_as_not_stored()
    {
        var store = new FakeAcknowledgements { Fails = true };

        var outcome = await Service(store).DismissAsync(ReleaseId, Correlated(Now.AddDays(-6)));

        Assert.Equal(UpdateFlagResult.NotStored, outcome.Result);
        Assert.False(outcome.Saved);
        Assert.Null(outcome.AcknowledgedThrough);
    }

    /// <summary>An unregistered store costs the control, never the panel.</summary>
    [Fact]
    public async Task With_nowhere_to_write_the_answer_is_still_an_answer()
    {
        var service = new UpdateFlagService(acknowledgements: null, clock: new FakeTimeProvider(Now));

        Assert.False((await service.DismissAsync(ReleaseId, Correlated(Now.AddDays(-6)))).Saved);
        Assert.False((await service.RestoreAsync(ReleaseId)).Saved);
        Assert.Null(await service.GetStandingAsync(ReleaseId));
    }

    [Fact]
    public async Task The_undo_revokes_rather_than_deleting()
    {
        var store = new FakeAcknowledgements { RevokeCount = 1 };

        var outcome = await Service(store).RestoreAsync(ReleaseId);

        Assert.True(outcome.Saved);

        // A revocation stamp against the release, carrying the clock. There is
        // no delete on the interface at all, which is the shape of the promise:
        // undo must not cost the history that makes this inspectable.
        Assert.Equal((ReleaseId, Now), Assert.Single(store.Revoked));
    }

    /// <summary>
    /// The standing watermark is what the panel opens on: it decides whether to
    /// offer the undo and which rows are already read. A read that fails
    /// answers null — which costs the undo and leaves every row lit, the
    /// direction to fail in. The alternative is hiding unread marks because a
    /// query hiccuped.
    /// </summary>
    [Fact]
    public async Task A_standing_watermark_is_read_back_and_a_failed_read_hides_nothing()
    {
        var through = Now.AddDays(-11);
        var standing = new UpdateAcknowledgement
        {
            ReleaseId = ReleaseId,
            AcknowledgedThrough = through,
            CreatedAt = Now,
        };

        Assert.Equal(through, await Service(new FakeAcknowledgements { Standing = standing }).GetStandingAsync(ReleaseId));
        Assert.Null(await Service(new FakeAcknowledgements()).GetStandingAsync(ReleaseId));
        Assert.Null(await Service(new FakeAcknowledgements { Fails = true }).GetStandingAsync(ReleaseId));
    }

    /// <summary>
    /// Nothing to revoke is not a failure: a newer correlated push may have
    /// outranked the watermark under the user's finger, in which case the undo
    /// was a no-op that still leaves them looking at the flag they wanted.
    /// </summary>
    [Fact]
    public async Task Revoking_nothing_is_not_reported_as_a_failure()
    {
        var outcome = await Service(new FakeAcknowledgements { RevokeCount = 0 }).RestoreAsync(ReleaseId);

        Assert.Equal(UpdateFlagResult.NothingToDo, outcome.Result);
    }

    // ══ The panel ═══════════════════════════════════════════════════════════

    /// <summary>
    /// §2's invariant, on the surface where it is visible. Rows at or below the
    /// watermark stop wearing Flare; a genuinely newer patch keeps it. The
    /// boundary is inclusive because the watermark IS the dismissed push's own
    /// instant — an exclusive test would leave that row lit immediately after
    /// the click.
    /// </summary>
    [Fact]
    public void The_dots_quiet_at_or_below_the_watermark_and_stay_lit_above_it()
    {
        var watermark = Now.AddDays(-20);
        var updates = new[]
        {
            Announcement(Now.AddDays(-5)),
            Announcement(watermark),
            Announcement(Now.AddDays(-40)),
        };

        var details = Details(Tile(lastPlayed: Now.AddDays(-60)), updates, acknowledgedThrough: watermark);

        // All three landed in the gap, and that stays true — dismissing does not
        // un-land an update.
        Assert.All(details.Updates, u => Assert.True(u.IsSinceYouPlayed));
        Assert.Equal("SINCE YOU PLAYED", details.UpdatesLabel);

        Assert.True(details.Updates[0].IsUnread);
        Assert.False(details.Updates[1].IsUnread);
        Assert.False(details.Updates[2].IsUnread);

        // §10.2 draws the rail's marks in Flare because they are the same
        // unread signal plotted in time, so they quiet with the dots.
        Assert.Single(details.RailMarks);
        Assert.Equal("1 update landed while you were away.", details.GapCaption);
    }

    /// <summary>
    /// The watermark is a build push and the patch notes land a day later, so a
    /// bare "at or before the watermark" test would leave the Flare dot burning
    /// on the very row the user had just read. The push and its announcement are
    /// one update; they quiet together, and an announcement outside the
    /// correlation window belongs to a different one and does not.
    /// </summary>
    [Fact]
    public void The_announcement_that_corroborated_a_dismissed_push_quiets_with_it()
    {
        var pushedAt = Now.AddDays(-40);

        var events = new List<UpdateEvent>(Correlated(pushedAt))
        {
            // Five weeks past the watermark: a different update entirely.
            Announcement(Now.AddDays(-5)),
        };

        var details = Details(
            Tile(lastPlayed: Now.AddDays(-90), hasUnread: true),
            events,
            acknowledgedThrough: pushedAt,
            flags: Service(new FakeAcknowledgements()));

        // Newest first: the unrelated announcement, then the corroborating one,
        // then the push.
        Assert.True(details.Updates[0].IsUnread);
        Assert.False(details.Updates[1].IsUnread);
        Assert.False(details.Updates[2].IsUnread);
        Assert.Single(details.RailMarks);
    }

    /// <summary>
    /// With everything acknowledged the rail has no marks, and the words under
    /// it have to say why. §10.2 requires the rail to be restated underneath,
    /// and "No updates recorded in that stretch" would be false.
    /// </summary>
    [Fact]
    public void A_fully_read_gap_says_so_rather_than_claiming_nothing_shipped()
    {
        var updates = new[] { Announcement(Now.AddDays(-5)), Announcement(Now.AddDays(-9)) };

        var details = Details(
            Tile(lastPlayed: Now.AddDays(-60)), updates, acknowledgedThrough: Now.AddDays(-4));

        Assert.Empty(details.RailMarks);
        Assert.DoesNotContain(details.Updates, u => u.IsUnread);
        Assert.Equal("2 updates landed while you were away. You've marked them read.", details.GapCaption);
    }

    /// <summary>
    /// With no dismissal anywhere the panel is exactly what it was — the
    /// existing copy, the existing marks. The feature costs the ordinary case
    /// nothing.
    /// </summary>
    [Fact]
    public void An_undismissed_gap_is_unchanged()
    {
        var updates = new[] { Announcement(Now.AddDays(-5)), Announcement(Now.AddDays(-9)) };

        var details = Details(Tile(lastPlayed: Now.AddDays(-60)), updates);

        Assert.Equal(2, details.RailMarks.Count);
        Assert.All(details.Updates, u => Assert.True(u.IsUnread));
        Assert.Equal("2 updates landed while you were away.", details.GapCaption);
    }

    /// <summary>
    /// The control is offered on the flag being up — bucket membership, which
    /// the tile carries — and swaps for the undo once a dismissal has landed.
    /// </summary>
    [Fact]
    public async Task Dismissing_quiets_the_dots_offers_the_undo_and_reloads_the_library()
    {
        var pushedAt = Now.AddDays(-11);
        var store = new FakeAcknowledgements();
        var reloads = 0;

        var details = Details(
            Tile(lastPlayed: Now.AddDays(-60), hasUnread: true),
            Correlated(pushedAt),
            flags: Service(store),
            reload: () => { reloads++; return Task.CompletedTask; });

        Assert.True(details.ShowDismissFlag);
        Assert.False(details.ShowRestoreFlag);
        Assert.NotEmpty(details.RailMarks);

        await details.DismissFlagCommand.ExecuteAsync(null);

        Assert.False(details.ShowDismissFlag);
        Assert.True(details.ShowRestoreFlag);
        Assert.Null(details.FlagProblem);

        // The push and the announcement both sit at or below the watermark, so
        // both rows go quiet and the rail empties with them.
        Assert.DoesNotContain(details.Updates, u => u.IsUnread);
        Assert.Empty(details.RailMarks);

        // §5.2 makes the badge bucket membership, so the rail's count and the
        // tile's dot are wrong until the query runs again.
        Assert.Equal(1, reloads);
    }

    /// <summary>
    /// The one lie this surface cannot tell. A failed write leaves the control
    /// exactly where it was, the dots exactly as they were, the library
    /// unreloaded — and says so.
    /// </summary>
    [Fact]
    public async Task A_failed_store_shows_no_receipt_and_changes_nothing()
    {
        var store = new FakeAcknowledgements { Fails = true };
        var reloads = 0;

        var details = Details(
            Tile(lastPlayed: Now.AddDays(-60), hasUnread: true),
            Correlated(Now.AddDays(-11)),
            flags: Service(store),
            reload: () => { reloads++; return Task.CompletedTask; });

        await details.DismissFlagCommand.ExecuteAsync(null);

        Assert.True(details.ShowDismissFlag);
        Assert.False(details.ShowRestoreFlag);
        Assert.False(details.DismissalStands);
        Assert.Equal("Couldn't save that — nothing changed.", details.FlagProblem);

        Assert.Contains(details.Updates, u => u.IsUnread);
        Assert.NotEmpty(details.RailMarks);
        Assert.Equal(0, reloads);
        Assert.Empty(store.Recorded);
    }

    /// <summary>
    /// A panel opened on a release whose flag a dismissal is holding down
    /// offers the way back, and only that.
    /// </summary>
    [Fact]
    public async Task A_standing_dismissal_offers_the_undo_and_taking_it_puts_the_flag_back()
    {
        var pushedAt = Now.AddDays(-11);
        var store = new FakeAcknowledgements { RevokeCount = 1 };
        var reloads = 0;

        // hasUnread is false: the bucket query has already dropped this game out
        // of stale_but_patched, which is the only thing that decides the dot.
        var details = Details(
            Tile(lastPlayed: Now.AddDays(-60), hasUnread: false),
            Correlated(pushedAt),
            acknowledgedThrough: pushedAt,
            flags: Service(store),
            reload: () => { reloads++; return Task.CompletedTask; });

        Assert.False(details.ShowDismissFlag);
        Assert.True(details.ShowRestoreFlag);
        Assert.DoesNotContain(details.Updates, u => u.IsUnread);

        await details.RestoreFlagCommand.ExecuteAsync(null);

        Assert.True(details.ShowDismissFlag);
        Assert.False(details.ShowRestoreFlag);
        Assert.Contains(details.Updates, u => u.IsUnread);
        Assert.NotEmpty(details.RailMarks);
        Assert.Equal(1, reloads);
        Assert.Single(store.Revoked);
    }

    /// <summary>
    /// Standing is not suppressing. A newer correlated push has re-raised the
    /// flag over an older dismissal, and what the user needs then is the way to
    /// dismiss the NEW patch — not an undo of one that is already having no
    /// effect.
    /// </summary>
    [Fact]
    public void A_newer_push_over_a_standing_dismissal_offers_the_dismissal_again()
    {
        var dismissed = Now.AddDays(-40);

        var details = Details(
            Tile(lastPlayed: Now.AddDays(-90), hasUnread: true),
            [Announcement(dismissed), Announcement(Now.AddDays(-3))],
            acknowledgedThrough: dismissed,
            flags: Service(new FakeAcknowledgements()));

        Assert.True(details.ShowDismissFlag);
        Assert.False(details.ShowRestoreFlag);
        Assert.True(details.DismissalStands);

        // Only the newer row is unread, which is the same answer the bucket
        // query gave when it left the game flagged.
        Assert.Single(details.Updates, u => u.IsUnread);
    }

    /// <summary>
    /// With no service registered there is no control, whatever the flag says.
    /// Offering a button and swallowing the click is the one thing not allowed.
    /// </summary>
    [Fact]
    public void With_no_service_the_control_is_not_offered()
    {
        var details = Details(Tile(lastPlayed: Now.AddDays(-60), hasUnread: true), Correlated(Now.AddDays(-11)));

        Assert.False(details.ShowDismissFlag);
        Assert.False(details.ShowRestoreFlag);
        Assert.False(details.ShowFlagControl);
    }

    // ══ Helpers ═════════════════════════════════════════════════════════════

    private static UpdateFlagService Service(IUpdateAcknowledgementRepository store)
        => new(store, BucketThresholds.Default, new FakeTimeProvider(Now));

    /// <summary>A build push and an announcement a day apart — one major update.</summary>
    private static List<UpdateEvent> Correlated(DateTime pushedAt)
        => [Push(pushedAt), Announcement(pushedAt.AddDays(1))];

    private static UpdateEvent Push(DateTime occurredAt) => new()
    {
        ReleaseId = ReleaseId,
        Kind = UpdateEventKinds.BuildPush,
        OccurredAt = occurredAt,
        BuildId = "19583201",
    };

    private static UpdateEvent Announcement(DateTime occurredAt) => new()
    {
        ReleaseId = ReleaseId,
        Kind = UpdateEventKinds.Announcement,
        OccurredAt = occurredAt,
        Title = "Patch notes",
    };

    private static GameTileViewModel Tile(DateTime? lastPlayed = null, bool hasUnread = false)
        => TileFixture.Tile(
            nowUtc: Now,
            ownershipId: 1,
            releaseId: ReleaseId,
            title: "Empyrion: Galactic Survival",
            bucket: hasUnread ? LibraryBuckets.StaleButPatched : LibraryBuckets.Bounced,
            playtimeMinutes: 600,
            lastPlayedUtc: lastPlayed,
            majorUpdateAt: hasUnread ? (lastPlayed?.AddMonths(9) ?? Now.AddDays(-1)) : null,
            steamAppId: "383120");

    /// <summary>
    /// The panel, built the way <c>LibraryViewModel.OpenDetailsAsync</c> builds
    /// it: the rows rendered newest first, and the raw events handed alongside
    /// them for the service to compute a watermark from.
    /// </summary>
    private static GameDetailsViewModel Details(
        GameTileViewModel tile,
        IReadOnlyList<UpdateEvent> events,
        DateTime? acknowledgedThrough = null,
        IUpdateFlagService? flags = null,
        Func<Task>? reload = null)
    {
        var rows = events
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => UpdateEventViewModel.Create(e, tile.LastPlayedUtc))
            .ToList();

        return new GameDetailsViewModel(
            tile,
            "Patched since",
            rows,
            Now,
            updateEvents: events,
            acknowledgedThrough: acknowledgedThrough,
            updateFlags: flags,
            reloadLibrary: reload);
    }

    /// <summary>
    /// The acknowledgement store, faked so that "the write threw" is a state a
    /// test can produce — which is the state the real repository will not
    /// produce on demand and the one the panel must not lie about.
    /// </summary>
    private sealed class FakeAcknowledgements : IUpdateAcknowledgementRepository
    {
        public List<UpdateAcknowledgement> Recorded { get; } = [];

        public List<(long ReleaseId, DateTime RevokedAt)> Revoked { get; } = [];

        public bool Fails { get; init; }

        public int RevokeCount { get; init; }

        public UpdateAcknowledgement? Standing { get; init; }

        public Task<long> RecordAsync(UpdateAcknowledgement ack, CancellationToken ct = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("database is locked");
            }

            Recorded.Add(ack);
            return Task.FromResult((long)Recorded.Count);
        }

        public Task<int> RevokeAsync(long releaseId, DateTime revokedAtUtc, CancellationToken ct = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("database is locked");
            }

            Revoked.Add((releaseId, revokedAtUtc));
            return Task.FromResult(RevokeCount);
        }

        public Task<UpdateAcknowledgement?> GetStandingAsync(long releaseId, CancellationToken ct = default)
            => Fails
                ? throw new InvalidOperationException("database is locked")
                : Task.FromResult(Standing);
    }
}
