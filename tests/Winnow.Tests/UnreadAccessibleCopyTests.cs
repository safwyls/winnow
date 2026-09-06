using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The unread signal in words: what the tile, the rail row and the feed card
/// say about it, and where the count they say it with comes from
/// (design-system.md §5.2, §8).
///
/// <para>§8 calls the dormancy ramp decorative-redundant and means it — idle
/// time is also text on hover and a sortable column — and the store mark has
/// chips and a tooltip behind it. The Flare dot had nothing. It was the one
/// thing on the cover wall a screen reader could not reach by any route, which
/// is what these tests are here to keep from being true again.</para>
///
/// <para>The count has to be spelled into the name string. Avalonia compiles
/// <c>AutomationProperties.PositionInSet</c> and <c>SizeOfSet</c> and reads
/// neither, so "3 of 12" is not expressible and the wording is the only place a
/// count can live. That is why a test on copy is a test on behaviour here.</para>
/// </summary>
public sealed class UnreadAccessibleCopyTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, DateTimeKind.Utc);

    // ══ The tile ════════════════════════════════════════════════════════════

    /// <summary>
    /// The whole of the fix in one assertion: a badged tile names the game and
    /// then says what the dot on it means, with the count in it. Pinning the
    /// count as well as the phrase is deliberate — a name that said only
    /// "patched" would pass a laxer test while losing the one number the badge
    /// is worth reading.
    /// </summary>
    [Fact]
    public void A_patched_tile_says_so_and_says_how_many()
    {
        var tile = TileFixture.Tile(
            Now,
            title: "Deep Rock Galactic",
            bucket: LibraryBuckets.StaleButPatched,
            playtimeMinutes: 600,
            lastPlayedUtc: Utc(2024, 3, 1),
            majorUpdateAt: Utc(2026, 8, 20),
            unreadUpdateCount: 3);

        Assert.True(tile.HasUnread);
        Assert.Equal(3, tile.UnreadUpdateCount);
        Assert.Contains("Deep Rock Galactic", tile.AutomationName, StringComparison.Ordinal);
        Assert.Contains("3 updates", tile.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    /// One is not "1 updates". A single patch is an ordinary case rather than an
    /// edge one, so the singular branch is heard as often as the plural, and an
    /// app that fumbles it out loud sounds like one that is guessing (§7).
    /// </summary>
    [Fact]
    public void One_update_is_worded_as_one()
    {
        var tile = TileFixture.Tile(
            Now,
            bucket: LibraryBuckets.StaleButPatched,
            playtimeMinutes: 600,
            lastPlayedUtc: Utc(2024, 3, 1),
            majorUpdateAt: Utc(2026, 8, 20),
            unreadUpdateCount: 1);

        Assert.Contains("1 update", tile.AutomationName, StringComparison.Ordinal);
        Assert.DoesNotContain("1 updates", tile.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The badge is bucket membership and the count is a separate column, so
    /// they can be apart. What is pinned is which way the copy falls when they
    /// are: the dot still says what it means, and never announces a bare zero
    /// beside a dot that is plainly drawn.
    /// </summary>
    [Fact]
    public void A_badge_with_no_count_still_states_the_badge()
    {
        var tile = TileFixture.Tile(
            Now,
            bucket: LibraryBuckets.StaleButPatched,
            playtimeMinutes: 600,
            lastPlayedUtc: Utc(2024, 3, 1),
            majorUpdateAt: Utc(2026, 8, 20),
            unreadUpdateCount: 0);

        Assert.True(tile.HasUnread);
        Assert.NotEqual(tile.Title, tile.AutomationName);
        Assert.DoesNotContain("0 update", tile.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the copy, and the one that keeps the wall readable: a
    /// tile with no badge gains no words at all. Its name is the title, exactly.
    /// A reader arrowing across six hundred tiles hears the update clause only
    /// on the tiles that have one, which is what makes hearing it mean anything.
    /// </summary>
    [Fact]
    public void A_tile_with_no_badge_says_nothing_about_updates()
    {
        var tile = TileFixture.Tile(Now, title: "Tunic", bucket: LibraryBuckets.NeverPlayed);

        Assert.False(tile.HasUnread);
        Assert.Equal(0, tile.UnreadUpdateCount);
        Assert.Equal("Tunic", tile.AutomationName);
    }

    // ══ The rail ════════════════════════════════════════════════════════════

    /// <summary>
    /// The rail row is a Button whose content is a Grid, and
    /// <c>ContentControlAutomationPeer.GetNameCore</c> falls back to
    /// <c>Content?.ToString()</c> — so a row with no name of its own announced
    /// "Avalonia.Controls.Grid". The last assertion is the one that would have
    /// caught it, and it is checked as an absence because the type name is what
    /// silence sounds like on a ContentControl.
    /// </summary>
    [Fact]
    public void The_patched_rail_row_announces_its_count_and_its_meaning()
    {
        var patched = new BucketViewModel(LibraryBuckets.StaleButPatched, "Patched", showsFlarePip: true)
        {
            Count = 12,
        };

        Assert.Contains("12", patched.AutomationName, StringComparison.Ordinal);
        Assert.Contains("Patched", patched.AutomationName, StringComparison.Ordinal);
        Assert.DoesNotContain("Avalonia", patched.AutomationName, StringComparison.Ordinal);
    }

    // ══ The feed card ═══════════════════════════════════════════════════════

    /// <summary>
    /// A feed card is its reason sentence — the game and why it is being put in
    /// front of you — and the sentence was reachable only by walking into the
    /// card's children. This pins that the card's own name carries both, so a
    /// reader can decide from the Tab stop rather than having to enter every
    /// card to find out what it is offering.
    /// </summary>
    [Fact]
    public void A_feed_card_names_itself_with_its_reason()
    {
        var card = new FeedCardViewModel(
            TileFixture.Tile(Now, title: "Outer Wilds", bucket: LibraryBuckets.NeverPlayed),
            "Bought 3 years ago, never opened.");

        Assert.Contains("Outer Wilds", card.AutomationName, StringComparison.Ordinal);
        Assert.Contains("never opened", card.AutomationName, StringComparison.Ordinal);
    }

    /// <summary>
    /// What is really pinned is the empty string. ItemStatus is the one attached
    /// property <c>ControlAutomationPeer</c> raises a UIA event for, which is why
    /// the verdict travels on it rather than in the name — and a card that
    /// reported a status before it had one would spend the whole feed announcing
    /// a state to every reader who passed it.
    /// </summary>
    [Fact]
    public void A_feed_card_reports_no_status_until_it_has_one()
    {
        var card = new FeedCardViewModel(
            TileFixture.Tile(Now, bucket: LibraryBuckets.NeverPlayed),
            "Bought 3 years ago, never opened.");

        Assert.Equal(string.Empty, card.StatusAnnouncement);

        card.IsSetAside = true;
        card.SetAsideNote = "Off the feed.";

        Assert.Contains("Off the feed.", card.StatusAnnouncement, StringComparison.Ordinal);
    }

    // ══ Where the count comes from ══════════════════════════════════════════

    /// <summary>
    /// The number the tile reads out has to be the number the dot stands for.
    /// The count comes out of the same <c>major_update</c> aggregate as the
    /// timestamp, under the same acknowledgement watermark and the same
    /// announcement correlation, so the fixture puts two decoys in front of it:
    /// a build push nobody announced, and an announcement with no build behind
    /// it. Neither flags the game (§4.5 wants both signals), so neither may be
    /// counted — a three here would be the dot standing for two patches while
    /// the words beside it claimed a third.
    /// </summary>
    [Fact]
    public async Task The_query_counts_only_the_pushes_the_badge_stands_for()
    {
        using var db = new TempDatabase();

        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownerships = new OwnershipRepository(db.Factory);
        var plays = new PlayRecordRepository(db.Factory);
        var updates = new UpdateEventRepository(db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Fixture", FirstReleaseYear = 2020 });
        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = "Fixture",
            Platform = "windows",
        });
        var ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });

        await plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 600,
            LastPlayedAt = Utc(2024, 1, 15),
            Source = "steam_local",
            ObservedAt = Utc(2026, 8, 1),
        });

        // Two correlated pushes, and two decoys that must not be counted: a
        // build push nobody announced, and an announcement with no build behind
        // it.
        foreach (var day in new[] { Utc(2026, 5, 4), Utc(2026, 7, 6) })
        {
            await updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.BuildPush,
                BuildId = "42",
                OccurredAt = day,
            });
            await updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.Announcement,
                OccurredAt = day.AddDays(1),
                Title = "Patch notes",
            });
        }

        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = "43",
            OccurredAt = Utc(2026, 3, 2),
        });
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = Utc(2026, 1, 9),
            Title = "Marketing",
        });

        var rows = await new LibraryQueryRepository(db.Factory)
            .GetOwnershipBucketsAsync(BucketThresholds.Default);

        var row = Assert.Single(rows, r => r.OwnershipId == ownershipId);

        Assert.Equal(LibraryBuckets.StaleButPatched, row.Bucket);
        Assert.Equal(2, row.Game.UnreadUpdateCount);
    }
}
