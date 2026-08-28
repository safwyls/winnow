using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="LibraryFilter"/> — the shared meaning of "what is selected",
/// used both by the filter panel and by live-list membership.
///
/// <para>Two properties matter more than the rest and are tested hardest. An
/// EMPTY filter matches everything, because "nothing selected" and "nothing
/// matches" must never be confused. And <see cref="LibraryFilter.FromJson"/> is
/// TOTAL: the value comes off disk inside a list the user made, so a parse
/// failure that throws is a list that disappears.</para>
/// </summary>
public class LibraryFilterTests
{
    [Fact]
    public void Empty_filter_is_empty_and_matches_every_row()
    {
        Assert.True(LibraryFilter.Empty.IsEmpty);
        Assert.True(new LibraryFilter().IsEmpty);

        var rows = new[] { Row(1), Row(2, bucket: LibraryBuckets.Retired, store: "gog") };

        Assert.Equal(rows, LibraryFilter.Empty.Apply(rows));
        Assert.All(rows, r => Assert.True(LibraryFilter.Empty.Matches(r)));
    }

    [Fact]
    public void A_filter_with_any_field_set_is_not_empty()
    {
        Assert.False(new LibraryFilter { Buckets = [LibraryBuckets.Bounced] }.IsEmpty);
        Assert.False(new LibraryFilter { GenreIds = [7] }.IsEmpty);
        Assert.False(new LibraryFilter { GameModes = [GameModes.CoOperative] }.IsEmpty);
        Assert.False(new LibraryFilter { Installed = false }.IsEmpty);
        Assert.False(new LibraryFilter { HasUnread = false }.IsEmpty);
        Assert.False(new LibraryFilter { YearFrom = 2010 }.IsEmpty);
        Assert.False(new LibraryFilter { Search = "skyrim" }.IsEmpty);
    }

    /// <summary>Whitespace is not a search term; it is a user mid-keystroke.</summary>
    [Fact]
    public void Whitespace_search_is_no_filter_at_all()
    {
        Assert.True(new LibraryFilter { Search = "   " }.IsEmpty);
    }

    // -- combination semantics ----------------------------------------------

    /// <summary>Within one field, "any of".</summary>
    [Fact]
    public void Values_within_a_field_widen_the_result()
    {
        var filter = new LibraryFilter { Buckets = [LibraryBuckets.Bounced, LibraryBuckets.Retired] };

        var matched = filter.Apply([
            Row(1, bucket: LibraryBuckets.Bounced),
            Row(2, bucket: LibraryBuckets.Retired),
            Row(3, bucket: LibraryBuckets.NeverPlayed),
        ]);

        Assert.Equal([1L, 2L], matched.Select(r => r.ReleaseId));
    }

    /// <summary>Across fields, "all of".</summary>
    [Fact]
    public void Different_fields_narrow_the_result()
    {
        var filter = new LibraryFilter
        {
            Buckets = [LibraryBuckets.Bounced],
            Stores = ["steam"],
        };

        var matched = filter.Apply([
            Row(1, bucket: LibraryBuckets.Bounced, store: "steam"),
            Row(2, bucket: LibraryBuckets.Bounced, store: "gog"),
            Row(3, bucket: LibraryBuckets.NeverPlayed, store: "steam"),
        ]);

        Assert.Equal([1L], matched.Select(r => r.ReleaseId));
    }

    /// <summary>
    /// The three facet-id fields share one id namespace but are three separate
    /// constraints: naming a genre and a tag requires both, and merging them into
    /// one set would silently turn that into "either", which is the one mistake
    /// here that would produce plausible wrong answers rather than obvious ones.
    /// </summary>
    [Fact]
    public void Genre_and_tag_selections_are_conjunctive()
    {
        var filter = new LibraryFilter { GenreIds = [10], TagIds = [20] };

        var matched = filter.Apply([
            Row(1, facetIds: [10, 20]),
            Row(2, facetIds: [10]),
            Row(3, facetIds: [20]),
        ]);

        Assert.Equal([1L], matched.Select(r => r.ReleaseId));
    }

    [Fact]
    public void Game_modes_match_on_slug()
    {
        var filter = new LibraryFilter { GameModes = [GameModes.CoOperative] };

        var matched = filter.Apply([
            Row(1, gameModes: [GameModes.SinglePlayer, GameModes.CoOperative]),
            Row(2, gameModes: [GameModes.SinglePlayer]),
            Row(3, gameModes: []),
        ]);

        Assert.Equal([1L], matched.Select(r => r.ReleaseId));
    }

    [Fact]
    public void Installed_and_unread_distinguish_false_from_unset()
    {
        var rows = new[] { Row(1, installed: true), Row(2, installed: false) };

        Assert.Equal([1L], new LibraryFilter { Installed = true }.Apply(rows).Select(r => r.ReleaseId));
        Assert.Equal([2L], new LibraryFilter { Installed = false }.Apply(rows).Select(r => r.ReleaseId));
        Assert.Equal(2, new LibraryFilter { Installed = null }.Apply(rows).Count);
    }

    /// <summary>
    /// An unknown year is not evidence that a game falls inside the window.
    /// Including it would make the count wrong in the direction nobody checks.
    /// </summary>
    [Fact]
    public void A_year_range_excludes_rows_with_no_known_year()
    {
        var rows = new[] { Row(1, year: 2011), Row(2, year: 2020), Row(3, year: null) };

        var matched = new LibraryFilter { YearFrom = 2010, YearTo = 2015 }.Apply(rows);

        Assert.Equal([1L], matched.Select(r => r.ReleaseId));
    }

    [Fact]
    public void Search_is_a_case_insensitive_substring_of_the_title()
    {
        var rows = new[] { Row(1, title: "The Elder Scrolls V: Skyrim"), Row(2, title: "Portal 2") };

        Assert.Equal([1L], new LibraryFilter { Search = "skyrim" }.Apply(rows).Select(r => r.ReleaseId));
        Assert.Equal([1L], new LibraryFilter { Search = "  SCROLLS " }.Apply(rows).Select(r => r.ReleaseId));
        Assert.Empty(new LibraryFilter { Search = "half-life" }.Apply(rows));
    }

    // -- serialization -------------------------------------------------------

    [Fact]
    public void Round_trips_through_json()
    {
        var filter = new LibraryFilter
        {
            Buckets = [LibraryBuckets.Bounced, LibraryBuckets.StaleButPatched],
            Stores = ["steam"],
            GenreIds = [3, 9],
            ThemeIds = [12],
            TagIds = [400, 401],
            GameModes = [GameModes.SinglePlayer, GameModes.Mmo],
            Installed = true,
            HasUnread = false,
            YearFrom = 2005,
            YearTo = 2024,
            Search = "elder",
        };

        var restored = LibraryFilter.FromJson(filter.ToJson());

        Assert.Equal(filter, restored);
    }

    [Fact]
    public void Empty_round_trips_to_empty()
    {
        Assert.True(LibraryFilter.FromJson(LibraryFilter.Empty.ToJson()).IsEmpty);
    }

    /// <summary>
    /// The stored form is snake_case so a database opened by hand reads as
    /// English, and the shape is worth pinning: it is what a future version has
    /// to keep being able to read.
    /// </summary>
    [Fact]
    public void Serializes_field_names_in_snake_case()
    {
        var json = new LibraryFilter { GenreIds = [3], YearFrom = 2005 }.ToJson();

        Assert.Contains("\"genre_ids\"", json, StringComparison.Ordinal);
        Assert.Contains("\"year_from\"", json, StringComparison.Ordinal);

        // IsEmpty is computed, and must never be written as if it were state.
        Assert.DoesNotContain("is_empty", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The forward-compatibility requirement, stated as a test: a filter saved
    /// by a build that knows fields this one does not must still load. A stored
    /// list that throws is a list the user loses.
    /// </summary>
    [Fact]
    public void Ignores_fields_a_future_version_added()
    {
        const string fromTheFuture = """
            {
              "buckets": ["bounced"],
              "genre_ids": [7],
              "developer_ids": [1, 2, 3],
              "deck_verified": true,
              "nested": { "anything": ["at", "all"] }
            }
            """;

        var filter = LibraryFilter.FromJson(fromTheFuture);

        Assert.Equal([LibraryBuckets.Bounced], filter.Buckets);
        Assert.Equal([7L], filter.GenreIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"buckets\": ")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"a string\"")]
    [InlineData("{\"genre_ids\": \"not a list\"}")]
    [InlineData("{\"genre_ids\": [{\"an\": \"object\"}]}")]
    [InlineData("{\"year_from\": \"nineteen ninety\"}")]
    [InlineData("{\"installed\": \"maybe\"}")]
    public void Never_throws_on_anything(string? json)
    {
        var filter = LibraryFilter.FromJson(json);

        // Whatever went wrong, the caller gets a usable filter — and the safe
        // failure is the whole library, never an empty list that looks like
        // data loss.
        Assert.NotNull(filter);
        Assert.True(filter.IsEmpty);
    }

    /// <summary>
    /// A field with the right name and the wrong type degrades that field alone
    /// where it can, but never at the cost of throwing.
    /// </summary>
    [Fact]
    public void Absent_fields_take_their_defaults()
    {
        var filter = LibraryFilter.FromJson("""{"search": "portal"}""");

        Assert.Equal("portal", filter.Search);
        Assert.Empty(filter.Buckets);
        Assert.Empty(filter.GenreIds);
        Assert.Null(filter.Installed);
        Assert.Null(filter.YearFrom);
        Assert.False(filter.IsEmpty);
    }

    private static FilterableRow Row(
        long releaseId,
        string bucket = LibraryBuckets.NeverPlayed,
        string store = "steam",
        string title = "Fixture",
        bool installed = false,
        bool hasUnread = false,
        int? year = 2015,
        long[]? facetIds = null,
        string[]? gameModes = null)
        => new(
            releaseId,
            OwnershipId: releaseId,
            bucket,
            store,
            title,
            installed,
            hasUnread,
            year,
            facetIds ?? [],
            gameModes ?? []);
}
