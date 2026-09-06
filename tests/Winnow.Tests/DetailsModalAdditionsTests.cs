using System.Reflection;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The lifetime axis computation: which snapshots are kept, what makes an axis
/// drawable, and what the two zones contain.
/// </summary>
public sealed class PlayAxisSeriesTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime MonthEnd(int year, int month)
        => new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddSeconds(-1);

    private static PlaytimeSnapshot At(DateTime observedAt, long minutes)
        => new() { OwnershipId = 1, PlaytimeMinutes = minutes, ObservedAt = observedAt };

    [Fact]
    public void No_release_year_means_no_axis()
    {
        var series = PlayAxisSeries.Build(
            [At(MonthEnd(2022, 7), 600), At(MonthEnd(2022, 8), 660)],
            releaseYear: null,
            lastPlayedUtc: Now.AddYears(-2),
            markTimesUtc: [],
            Now);

        Assert.False(series.CanDraw);
    }

    [Fact]
    public void One_month_end_reading_is_not_a_measured_month()
    {
        var series = PlayAxisSeries.Build(
            [At(MonthEnd(2022, 7), 600)],
            releaseYear: 2015,
            lastPlayedUtc: Now.AddYears(-2),
            markTimesUtc: [],
            Now);

        Assert.False(series.CanDraw);
    }

    [Fact]
    public void Live_snapshots_are_never_differenced_into_months()
    {
        // A snapshot is written only while Winnow is running, so a user who
        // closed it for three weeks gets three weeks of play stamped on one
        // instant. Only a month-end reading is genuinely per-month, so the
        // arbitrary instants contribute no bar at all.
        var series = PlayAxisSeries.Build(
            [
                At(new DateTime(2026, 4, 11, 9, 14, 3, DateTimeKind.Utc), 100),
                At(new DateTime(2026, 5, 2, 18, 41, 55, DateTimeKind.Utc), 400),
                At(new DateTime(2026, 6, 19, 7, 2, 0, DateTimeKind.Utc), 900),
            ],
            releaseYear: 2015,
            lastPlayedUtc: Now.AddYears(-2),
            markTimesUtc: [],
            Now);

        Assert.False(series.CanDraw);
    }

    [Fact]
    public void The_floor_point_is_an_amount_with_no_shape()
    {
        var series = PlayAxisSeries.Build(
            [
                At(MonthEnd(2022, 7), 11_400),
                At(MonthEnd(2022, 8), 11_640),
                At(MonthEnd(2022, 9), 11_640),
                At(MonthEnd(2022, 10), 12_240),
            ],
            releaseYear: 2015,
            lastPlayedUtc: new DateTime(2017, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            markTimesUtc: [],
            Now);

        Assert.True(series.CanDraw);
        Assert.True(series.HasUnmeasured);

        // Everything the covered months do not explain, and it is one figure:
        // no bar is drawn across the span it covers.
        Assert.Equal(11_400, series.UnmeasuredMinutes);

        // Three stretches between four readings, and their hours are the
        // differences between consecutive cumulative readings.
        Assert.Equal(3, series.Bars.Count);
        Assert.Equal(4.0, series.Bars[0].Hours, 3);
        Assert.Equal(0.0, series.Bars[1].Hours, 3);
        Assert.Equal(10.0, series.Bars[2].Hours, 3);

        // Every bar sits after the coverage boundary, so nothing is drawn over
        // the span whose shape was never measured.
        Assert.All(series.Bars, bar => Assert.True(bar.Start >= series.UnmeasuredFraction - 1e-9));
    }

    [Fact]
    public void The_axis_runs_from_the_release_year_to_today()
    {
        var series = PlayAxisSeries.Build(
            [At(MonthEnd(2022, 7), 600), At(MonthEnd(2022, 8), 660)],
            releaseYear: 2015,
            lastPlayedUtc: new DateTime(2017, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            markTimesUtc: [],
            Now);

        Assert.Equal(new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc), series.AxisStartUtc);
        Assert.Equal(MonthEnd(2022, 7), series.CoverageStartUtc);

        // Jan 2015 to Sep 2026 is about 140 months; Aug 2022 is about 91 of
        // them in, so the unmeasured zone is most of the axis.
        Assert.InRange(series.UnmeasuredFraction, 0.6, 0.7);

        // The last session is early on the axis, which is the whole point of
        // drawing the axis from the release rather than from the session.
        Assert.NotNull(series.StopFraction);
        Assert.InRange(series.StopFraction!.Value, 0.13, 0.20);
    }

    [Fact]
    public void Marks_are_placed_on_the_whole_axis_and_capped()
    {
        var marks = Enumerable
            .Range(0, 20)
            .Select(i => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i * 20))
            .ToArray();

        var series = PlayAxisSeries.Build(
            [At(MonthEnd(2022, 7), 600), At(MonthEnd(2022, 8), 660)],
            releaseYear: 2015,
            lastPlayedUtc: new DateTime(2017, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            markTimesUtc: marks,
            Now);

        Assert.Equal(PlayAxisSeries.MaxMarks, series.Marks.Count);
        Assert.All(series.Marks, m => Assert.InRange(m, 0.0, 1.0));

        // Marks are ordered in time, so a rail that draws them left to right
        // draws them in the order they landed.
        Assert.Equal(series.Marks.OrderBy(m => m), series.Marks);
    }

    [Fact]
    public void A_series_that_starts_before_the_stated_release_year_extends_the_axis()
    {
        // A wrong or missing release year must not push a reading off the left
        // edge of the axis and hide it.
        var series = PlayAxisSeries.Build(
            [At(MonthEnd(2022, 7), 600), At(MonthEnd(2022, 8), 660)],
            releaseYear: 2024,
            lastPlayedUtc: new DateTime(2022, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            markTimesUtc: [],
            Now);

        Assert.True(series.CanDraw);
        Assert.Equal(new DateTime(2022, 7, 1, 0, 0, 0, DateTimeKind.Utc), series.AxisStartUtc);
    }
}

/// <summary>
/// The reception line: order, attribution, counts, blending prohibition and the
/// Steam label's hover-only placement.
/// </summary>
public sealed class GameReceptionViewModelTests
{
    private static WorkRating Rating(string source, double? score, int? count, string? label = null)
        => new()
        {
            WorkId = 1,
            Source = source,
            Score = score,
            RatingCount = count,
            Label = label,
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void No_rows_means_no_line()
    {
        Assert.Null(GameReceptionViewModel.From(null));
        Assert.Null(GameReceptionViewModel.From([]));
    }

    [Fact]
    public void A_source_with_no_figure_contributes_nothing()
    {
        var reception = GameReceptionViewModel.From(
        [
            Rating(RatingSources.IgdbUsers, score: null, count: null),
            Rating(RatingSources.Steam, 91, 41_203, "Very Positive"),
        ]);

        Assert.NotNull(reception);
        var figure = Assert.Single(reception!.Figures);
        Assert.Equal(GameReceptionCopy.SourceSteam, figure.Source);
    }

    [Fact]
    public void Every_figure_is_attributed_and_carries_its_count()
    {
        var reception = GameReceptionViewModel.From(
        [
            Rating(RatingSources.Steam, 91, 41_203, "Very Positive"),
            Rating(RatingSources.IgdbCritics, 85, 42),
            Rating(RatingSources.IgdbUsers, 78, 1_204),
        ]);

        Assert.NotNull(reception);
        Assert.Equal(3, reception!.Figures.Count);

        // Order is the order the design fixes: IGDB's own user body, IGDB's
        // aggregation of external critics, then Steam's reviewers.
        Assert.Equal(
            [
                GameReceptionCopy.SourceIgdbUsers,
                GameReceptionCopy.SourceIgdbCritics,
                GameReceptionCopy.SourceSteam,
            ],
            reception.Figures.Select(f => f.Source));

        Assert.All(reception.Figures, f => Assert.NotEmpty(f.Source));
        Assert.All(reception.Figures, f => Assert.NotEmpty(f.Count));
        Assert.All(reception.Figures, f => Assert.NotEmpty(f.AutomationName));

        // The count is on the line, not only in the tooltip: a 9 from four
        // people and a 9 from four thousand are different claims.
        Assert.Contains("1,204", reception.Figures[0].Count);
        Assert.Contains("42", reception.Figures[1].Count);
        Assert.Contains("41,203", reception.Figures[2].Count);

        // Nothing separates the first figure; every later one is set off from
        // the one before it.
        Assert.False(reception.Figures[0].IsSeparated);
        Assert.True(reception.Figures[1].IsSeparated);
        Assert.True(reception.Figures[2].IsSeparated);
    }

    [Fact]
    public void Steams_own_label_is_on_hover_and_not_on_the_line()
    {
        var reception = GameReceptionViewModel.From(
            [Rating(RatingSources.Steam, 91, 41_203, "Very Positive")]);

        var figure = Assert.Single(reception!.Figures);

        Assert.Equal("91%", figure.Value);
        Assert.DoesNotContain("Very Positive", figure.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Very Positive", figure.Count, StringComparison.Ordinal);
        Assert.Contains("Very Positive", figure.Tooltip, StringComparison.Ordinal);
        Assert.Contains("91", figure.Tooltip, StringComparison.Ordinal);
        Assert.Contains("41,203", figure.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_count_of_zero_is_no_figure_rather_than_a_score_from_nobody()
    {
        Assert.Null(GameReceptionViewModel.From([Rating(RatingSources.IgdbUsers, 90, 0)]));
    }

    [Fact]
    public void The_three_figures_are_never_blended()
    {
        var reception = GameReceptionViewModel.From(
        [
            Rating(RatingSources.IgdbUsers, 78, 1_204),
            Rating(RatingSources.IgdbCritics, 85, 42),
            Rating(RatingSources.Steam, 91, 41_203, "Very Positive"),
        ]);

        // Three figures, drawn as three, and none of them is an average of the
        // others: three populations answering three different questions.
        Assert.Equal(["78", "85", "91%"], reception!.Figures.Select(f => f.Value));
    }
}

/// <summary>
/// The screenshot strip: artwork exclusion, publisher order, the hero selection
/// model, and the "nothing rather than an empty frame" rule.
/// </summary>
public sealed class GameScreenshotsViewModelTests
{
    private static WorkImages Images(string kind, string ids)
        => new()
        {
            WorkId = 1,
            Source = ImageSources.Igdb,
            Kind = kind,
            ImageIds = ids,
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void No_screenshots_draws_nothing_rather_than_an_empty_frame()
    {
        Assert.Null(GameScreenshotsViewModel.From(null, covers: null));
        Assert.Null(GameScreenshotsViewModel.From([], covers: null));
    }

    [Fact]
    public void An_artwork_row_is_not_a_screenshot()
    {
        Assert.Null(GameScreenshotsViewModel.From([Images(ImageKinds.Artwork, "ab1,cd2")], covers: null));
    }

    [Fact]
    public void The_strip_keeps_the_publishers_order_and_states_its_own_count()
    {
        var shots = GameScreenshotsViewModel.From(
            [Images(ImageKinds.Screenshot, "co6m51,ab12cd,ef34gh")], covers: null);

        Assert.NotNull(shots);
        Assert.True(shots!.HasShots);
        Assert.Equal(3, shots.Shots.Count);
        Assert.Equal(GameScreenshotsCopy.Caption(3), shots.Caption);

        Assert.Equal(
            ["co6m51", "ab12cd", "ef34gh"],
            shots.Shots.Select(s => s.Key.Id));

        // The screenshot rendition, not the cover one: an IGDB cover is 3:4 and
        // a screenshot is 16:9, and the provider is what picks the size token.
        Assert.All(shots.Shots, s => Assert.Equal(CoverProviders.IgdbScreenshot, s.Key.Provider));
    }

    [Fact]
    public void Nothing_is_expanded_until_a_shot_is_picked()
    {
        var shots = GameScreenshotsViewModel.From(
            [Images(ImageKinds.Screenshot, "co6m51,ab12cd")], covers: null);

        Assert.False(shots!.HasHero);
        Assert.All(shots.Shots, s => Assert.False(s.IsSelected));

        shots.SelectCommand.Execute(shots.Shots[1]);

        Assert.False(shots.Shots[0].IsSelected);
        Assert.True(shots.Shots[1].IsSelected);
    }
}

/// <summary>
/// The refetch control: every outcome is a word, only a write reopens the modal,
/// and a carried confirmation survives the reopen.
/// </summary>
public sealed class GameRefetchViewModelTests
{
    private sealed class Fake(GameRefetchResult result) : IGameRefetch
    {
        public int Calls { get; private set; }

        public Task<GameRefetchResult> RefetchAsync(long workId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public void Nothing_is_said_until_something_is_asked()
    {
        var vm = new GameRefetchViewModel(new Fake(new GameRefetchResult(GameRefetchOutcome.NothingNew)), 1);

        Assert.False(vm.HasStatus);
        Assert.Null(vm.Status);
    }

    [Theory]
    [InlineData(GameRefetchOutcome.Updated, false)]
    [InlineData(GameRefetchOutcome.NothingNew, false)]
    [InlineData(GameRefetchOutcome.NotConfigured, true)]
    [InlineData(GameRefetchOutcome.Unreachable, true)]
    [InlineData(GameRefetchOutcome.NoSourceToAsk, true)]
    [InlineData(GameRefetchOutcome.WorkNotFound, true)]
    [InlineData(GameRefetchOutcome.TooSoon, true)]
    public async Task Every_outcome_is_a_word(GameRefetchOutcome outcome, bool isProblem)
    {
        var vm = new GameRefetchViewModel(
            new Fake(new GameRefetchResult(outcome) { RetryAfter = TimeSpan.FromMinutes(3) }), 1);

        await vm.RefetchCommand.ExecuteAsync(null);

        Assert.True(vm.HasStatus);
        Assert.NotEmpty(vm.Status!);

        // No total is knowable in advance, so no proportion could be honest.
        Assert.DoesNotContain('%', vm.Status!);
        Assert.Equal(isProblem, vm.IsProblem);
    }

    [Fact]
    public async Task Only_a_write_reopens_the_modal_and_carries_its_confirmation()
    {
        var carried = new List<string>();

        var updated = new GameRefetchViewModel(
            new Fake(new GameRefetchResult(GameRefetchOutcome.Updated)),
            1,
            note => { carried.Add(note); return Task.CompletedTask; });

        await updated.RefetchCommand.ExecuteAsync(null);
        Assert.Single(carried);
        Assert.Equal(GameRefetchCopy.Updated, carried[0]);

        var nothing = new GameRefetchViewModel(
            new Fake(new GameRefetchResult(GameRefetchOutcome.NothingNew)),
            1,
            note => { carried.Add(note); return Task.CompletedTask; });

        await nothing.RefetchCommand.ExecuteAsync(null);
        Assert.Single(carried);
    }

    [Fact]
    public void A_carried_confirmation_is_shown_on_the_reopened_modal()
    {
        var vm = new GameRefetchViewModel(
            new Fake(new GameRefetchResult(GameRefetchOutcome.NothingNew)),
            1,
            afterChange: null,
            note: GameRefetchCopy.Updated);

        Assert.True(vm.HasStatus);
        Assert.Equal(GameRefetchCopy.Updated, vm.Status);
    }
}

/// <summary>
/// The ACQUIRED block: earliest date, licence vocabulary, the unrecognised-type
/// rule, and the price exclusion.
/// </summary>
public sealed class GameAcquisitionViewModelTests
{
    private static Ownership Owned(DateTime? acquiredAt, string? licenseType, long? pricePaidCents)
        => new()
        {
            ReleaseId = 1,
            Store = "steam",
            AcquiredAt = acquiredAt,
            LicenseType = licenseType,
            PricePaidCents = pricePaidCents,
        };

    [Fact]
    public void Neither_fact_means_no_block()
    {
        Assert.Null(GameAcquisitionViewModel.From(null));
        Assert.Null(GameAcquisitionViewModel.From([]));
        Assert.Null(GameAcquisitionViewModel.From([Owned(null, null, 5999)]));
    }

    [Fact]
    public void The_earliest_licence_is_the_one_that_answers_when_you_got_it()
    {
        var acquisition = GameAcquisitionViewModel.From(
        [
            Owned(new DateTime(2019, 12, 21, 0, 0, 0, DateTimeKind.Utc), "steam_store", null),
            Owned(new DateTime(2016, 11, 4, 0, 0, 0, DateTimeKind.Utc), "gift", null),
        ]);

        Assert.NotNull(acquisition);
        Assert.True(acquisition!.HasDate);
        Assert.Contains("2016", acquisition.DateText, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognised_licence_says_nothing_rather_than_showing_a_stored_token()
    {
        var acquisition = GameAcquisitionViewModel.From(
            [Owned(new DateTime(2019, 12, 21, 0, 0, 0, DateTimeKind.Utc), "something_new", null)]);

        Assert.True(acquisition!.HasDate);
        Assert.False(acquisition.HasLicence);
        Assert.Empty(acquisition.LicenseText);
    }

    /// <summary>
    /// §7: never be smug. "$59.99 · never opened" is the sentence this product
    /// must not write, so the price column has no route into this modal at all
    /// — not as a property, not as a field, not as a constructor parameter.
    /// </summary>
    [Fact]
    public void Price_paid_has_no_route_into_the_modal()
    {
        Type[] surfaces =
        [
            typeof(GameAcquisitionViewModel),
            typeof(GameDetailsViewModel),
        ];

        foreach (var type in surfaces)
        {
            var members = type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .Where(name => name.Contains("Price", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Paid", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Cost", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(members.Count == 0, $"{type.Name} exposes {string.Join(", ", members)}");
        }
    }
}
