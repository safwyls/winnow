using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class RatingCapPreferenceTests
{
    private static readonly DateTime Observed = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── The slider ──────────────────────────────────────────────────────────

    [Fact]
    public void The_slider_spans_the_scale_and_starts_at_the_top()
    {
        var display = new DisplaySettingsViewModel(new DormancyRamp());

        Assert.Equal(MaturityTiers.Ordered, DisplaySettingsViewModel.CapSteps);
        Assert.Equal((double)(MaturityTiers.Ordered.Count - 1), DisplaySettingsViewModel.MaximumCapIndex);
        Assert.Equal(BucketThresholds.NoMaturityCap, display.MaturityCap);
        Assert.Equal(DisplaySettingsViewModel.MaximumCapIndex, display.MaturityCapIndex);
    }

    [Fact]
    public void Every_slider_position_names_one_step_of_the_scale()
    {
        var display = new DisplaySettingsViewModel(new DormancyRamp());

        for (var i = 0; i < DisplaySettingsViewModel.CapSteps.Count; i++)
        {
            display.MaturityCapIndex = i;

            Assert.Equal(DisplaySettingsViewModel.CapSteps[i], display.MaturityCap);
            Assert.Equal((double)i, display.MaturityCapIndex);
        }
    }

    [Theory]
    [InlineData(-4.0, 0)]
    [InlineData(0.4, 0)]
    [InlineData(2.5, 3)]
    [InlineData(99.0, 5)]
    public void A_position_between_or_beyond_the_steps_snaps_onto_one(double raw, int expected)
    {
        var display = new DisplaySettingsViewModel(new DormancyRamp())
        {
            MaturityCapIndex = raw,
        };

        Assert.Equal(DisplaySettingsViewModel.CapSteps[expected], display.MaturityCap);
    }

    // ── Persistence (AC5) ───────────────────────────────────────────────────

    [Fact]
    public async Task An_unset_cap_loads_as_no_cap_and_is_not_written_back()
    {
        var settings = new FakeSettings();
        var display = new DisplaySettingsViewModel(new DormancyRamp(), settings);

        await display.LoadAsync();

        Assert.Equal(BucketThresholds.NoMaturityCap, display.MaturityCap);
        Assert.Equal(0, settings.Writes);
    }

    [Fact]
    public async Task Moving_the_cap_writes_it_and_reloads_the_library()
    {
        var settings = new FakeSettings();
        var reloads = 0;
        var display = new DisplaySettingsViewModel(
            new DormancyRamp(), settings, reloadLibrary: () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        display.MaturityCap = MaturityTier.Teen;
        await display.PendingSave;

        Assert.Equal(1, reloads);
        Assert.Equal(
            BucketThresholds.FormatMaturityCap(MaturityTier.Teen),
            await settings.GetAsync(BucketThresholds.MaturityCapSettingKey));
    }

    [Fact]
    public async Task An_unreadable_stored_cap_loads_as_no_cap()
    {
        var settings = new FakeSettings();
        settings.Seed(BucketThresholds.MaturityCapSettingKey, "pretty tame please");

        var display = new DisplaySettingsViewModel(new DormancyRamp(), settings);
        await display.LoadAsync();

        Assert.Equal(BucketThresholds.NoMaturityCap, display.MaturityCap);
    }

    [Fact]
    public async Task The_cap_survives_a_restart()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);

        var first = new DisplaySettingsViewModel(new DormancyRamp(), settings);
        first.MaturityCap = MaturityTier.Mature;
        await first.PendingSave;

        var next = new DisplaySettingsViewModel(new DormancyRamp(), settings);
        await next.LoadAsync();

        Assert.Equal(MaturityTier.Mature, next.MaturityCap);
        Assert.Equal(3.0, next.MaturityCapIndex);
    }

    [Fact]
    public void The_cap_works_without_a_settings_store()
    {
        var display = new DisplaySettingsViewModel(new DormancyRamp())
        {
            MaturityCap = MaturityTier.Teen,
        };

        Assert.Equal(MaturityTier.Teen, display.MaturityCap);
    }

    // ── Composition with the 18+ setting (AC4) ──────────────────────────────

    [Fact]
    public async Task The_popover_learns_the_eighteen_plus_setting_from_the_same_row_it_is_stored_in()
    {
        var settings = new FakeSettings();
        var display = new DisplaySettingsViewModel(new DormancyRamp(), settings);

        await display.LoadAsync();
        Assert.False(display.AdultContentAllowed);

        settings.Seed(
            BucketThresholds.ShowExplicitContentSettingKey,
            BucketThresholds.FormatShowExplicitContent(true));
        await display.LoadAsync();

        Assert.True(display.AdultContentAllowed);
    }

    [Fact]
    public void The_top_step_reads_as_clamped_only_while_the_eighteen_plus_setting_is_off()
    {
        var display = new DisplaySettingsViewModel(new DormancyRamp());

        Assert.Equal(BucketThresholds.NoMaturityCap, display.MaturityCap);
        Assert.True(display.IsCapClamped);

        display.AdultContentAllowed = true;
        Assert.False(display.IsCapClamped);

        display.AdultContentAllowed = false;
        display.MaturityCap = MaturityTier.Restricted18;
        Assert.False(display.IsCapClamped);
    }

    // ── The count that says which control is hiding a game (AC4) ────────────

    [Fact]
    public async Task The_popover_reports_what_the_cap_alone_is_hiding()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        var library = new LibraryQueryRepository(db.Factory);

        var adult = await SeedGameAsync(db, "Nameless Game", MaturityRatingCodes.EsrbAdultsOnly);
        var violent = await SeedGameAsync(db, "Outer Wilds", MaturityRatingCodes.Pegi18);
        Assert.NotEqual(adult, violent);

        var display = new DisplaySettingsViewModel(
            new DormancyRamp(), settings, libraryQueries: library);

        await display.LoadAsync();
        Assert.Equal(0, display.CapHiddenCount);
        Assert.Equal(MaturityCapCopy.NothingHidden, display.CapHiddenText);

        display.MaturityCap = MaturityTier.Teen;
        await display.PendingSave;

        // Two games are off screen, but the adults-only one was already gone:
        // the 18+ setting took it before the cap was consulted.
        Assert.Equal(1, display.CapHiddenCount);
        Assert.NotEqual(MaturityCapCopy.NothingHidden, display.CapHiddenText);
    }

    private static async Task<long> SeedGameAsync(TempDatabase db, string name, string ratings)
    {
        var workId = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = name });
        var releaseId = await new ReleaseRepository(db.Factory)
            .InsertAsync(new Release { WorkId = workId, Name = name });
        await new OwnershipRepository(db.Factory)
            .InsertAsync(new Ownership { ReleaseId = releaseId, Store = "steam" });
        await new WorkMaturityRepository(db.Factory).UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = ratings,
            ObservedAt = Observed,
        });

        return workId;
    }

    /// <summary>In-memory settings store: what you wrote is what you read.</summary>
    private sealed class FakeSettings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            Writes++;
            return Task.CompletedTask;
        }

        public void Seed(string key, string value) => _values[key] = value;
    }
}
