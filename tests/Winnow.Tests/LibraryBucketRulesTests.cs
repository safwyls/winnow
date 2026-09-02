using Dapper;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The §6.1 bucket rules as a pure function (TASK-70.6). The CASE moved out of
/// the SQL into C# so it could be applied at two grains — once per ownership
/// row, once per game over the summed minutes — without becoming two
/// implementations. The risk of that move is a date disagreement, so the month
/// arithmetic is pinned against SQLite itself.
/// </summary>
public sealed class LibraryBucketRulesTests
{
    private static readonly BucketThresholds Thresholds = BucketThresholds.Default;

    [Fact]
    public void Nothing_played_and_no_date_is_never_played()
        => Assert.Equal(
            LibraryBuckets.NeverPlayed,
            LibraryBucketRules.Classify(0, null, null, Thresholds));

    [Fact]
    public void Zero_minutes_beside_a_real_date_is_not_never_played()
    {
        // A source that did not measure the session. Unknown minutes are
        // neither never-played nor bounced, so the row falls past every
        // playtime test.
        var bucket = LibraryBucketRules.Classify(
            0, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, Thresholds);

        Assert.Equal(LibraryBuckets.Active, bucket);
    }

    [Fact]
    public void Retired_outranks_a_patch()
    {
        var played = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bucket = LibraryBucketRules.Classify(
            10_000, played, played.AddYears(3), Thresholds);

        Assert.Equal(LibraryBuckets.Retired, bucket);
    }

    [Fact]
    public void A_patch_outranks_bounced()
    {
        var played = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bucket = LibraryBucketRules.Classify(
            300, played, played.AddYears(2), Thresholds);

        Assert.Equal(LibraryBuckets.StaleButPatched, bucket);
    }

    [Fact]
    public void Real_playtime_with_no_date_is_maximally_dormant_and_not_active()
    {
        // Steam's 86400 sentinel: played before Steam kept timestamps.
        var bucket = LibraryBucketRules.Classify(
            300, null, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), Thresholds);

        Assert.Equal(LibraryBuckets.StaleButPatched, bucket);
    }

    /// <summary>
    /// The reason the sum must be re-classified rather than the strongest
    /// member's bucket taken. Two entries at sixty minutes are two Active rows;
    /// the game they make is Bounced. A rule that folded buckets instead of
    /// minutes would file the game in the wrong pile, and it is exactly the
    /// pile the product is about.
    /// </summary>
    [Fact]
    public void Two_entries_under_the_refund_line_make_one_game_over_it()
    {
        var played = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(LibraryBuckets.Active, LibraryBucketRules.Classify(60, played, null, Thresholds));
        Assert.Equal(LibraryBuckets.Bounced, LibraryBucketRules.Classify(120, played, null, Thresholds));
    }

    /// <summary>
    /// The same fold through <c>GameGrouping.Of</c>, so the claim is about the
    /// code the repository actually runs and not a reimplementation of it.
    /// </summary>
    [Fact]
    public void The_grouping_classifies_the_sum_and_carries_the_groups_own_date()
    {
        var older = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // The HIGHER playtime carries the OLDER date, so a composite that
        // borrowed a date from one store would visibly show the wrong one.
        var entries = new[]
        {
            new Winnow.Core.Identity.CoverageEntry
            {
                OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = "Prey", Store = "steam",
                PlaytimeMinutes = 100, LastPlayedAt = older,
            },
            new Winnow.Core.Identity.CoverageEntry
            {
                OwnershipId = 2, ReleaseId = 2, WorkId = 2, Title = "Prey", Store = "epic",
                PlaytimeMinutes = 20, LastPlayedAt = newer,
            },
        };

        var game = GameGrouping.Of(1, entries, null, Thresholds);

        Assert.Equal(120, game.PlaytimeMinutes);
        Assert.Equal(newer, game.LastPlayedAt);
        Assert.Equal(2, game.EntryCount);
        Assert.True(game.IsCollapsed);
        Assert.Equal(LibraryBuckets.Bounced, game.Bucket);
    }

    /// <summary>
    /// SQLite's own <c>'+N months'</c>, asked of SQLite. The modifier adds to
    /// the month field and then normalises an out-of-range day into the
    /// following month (2024-03-31 + 6 = 2024-10-01), where .NET's
    /// <c>AddMonths</c> clamps to the last day (2024-09-30). Lifting the CASE
    /// out of SQL had to keep the SQL's answer; the comparison truncates to
    /// whole seconds because <c>datetime()</c> renders to whole seconds.
    /// </summary>
    [Theory]
    [InlineData("2024-03-31 00:00:00", 6)]
    [InlineData("2024-01-31 00:00:00", 1)]
    [InlineData("2024-08-31 00:00:00", 6)]
    [InlineData("2023-12-31 00:00:00", 2)]
    [InlineData("2024-02-29 00:00:00", 12)]
    [InlineData("2021-05-15 13:45:07", 6)]
    [InlineData("2020-01-01 00:00:00", 0)]
    [InlineData("2024-10-31 00:00:00", 4)]
    public void The_month_arithmetic_is_the_one_SQLite_applies(string stamp, int months)
    {
        using var db = new TempDatabase();
        using var lease = db.Factory.Lease();

        var sqlite = lease.Connection.QuerySingle<string>(
            "SELECT datetime(@Stamp, '+' || @Months || ' months');",
            new { Stamp = stamp, Months = months },
            lease.Transaction);

        var parsed = DateTime.Parse(stamp, System.Globalization.CultureInfo.InvariantCulture);
        var mine = LibraryBucketRules.AddMonths(parsed, months);

        Assert.Equal(sqlite, mine.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
    }
}
