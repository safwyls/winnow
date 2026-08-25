using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The "hide non-game entries" filter, as the library view meets it: through
/// <see cref="LibraryQueryRepository.GetOwnershipBucketsAsync"/>, driven by
/// <see cref="BucketThresholds.ShowNonGameEntries"/>.
///
/// <para>Three properties are load-bearing and each has its own tests here: a
/// NULL type is never filtered (most of the library has one, and hiding those
/// would empty the app); an unrecognised type is never filtered (Valve's
/// vocabulary grows); and demo consolidation answers identically with the
/// filter on and off.</para>
/// </summary>
public class NonGameFilterTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;

    public NonGameFilterTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> SeedAsync(string title, string? appType, long playtimeMinutes = 300)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = title,
            SteamAppType = appType,
        });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = title,
            Platform = "windows",
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "steam_local",
            ObservedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        return ownershipId;
    }

    private Task<IReadOnlyList<OwnershipBucket>> QueryAsync(bool showNonGameEntries)
        => new LibraryQueryRepository(_db.Factory).GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = showNonGameEntries });

    // ── The default, and the toggle ──────────────────────────────────────────

    /// <summary>
    /// The user's decision, in one test: Steam carries non-game items, this
    /// application is about games, so they are hidden unless asked for.
    /// </summary>
    [Fact]
    public async Task A_tool_is_hidden_by_default_and_returns_when_the_setting_is_on()
    {
        var game = await SeedAsync("Skyrim Special Edition", "Game");
        var tool = await SeedAsync("Skyrim Creation Kit", "Tool");

        var hidden = await QueryAsync(showNonGameEntries: false);
        Assert.Equal(game, Assert.Single(hidden).OwnershipId);

        var shown = await QueryAsync(showNonGameEntries: true);
        Assert.Equal(2, shown.Count);
        Assert.Contains(shown, r => r.OwnershipId == tool);
    }

    /// <summary>
    /// Nothing is written and nothing is deleted, so the toggle is a read-time
    /// decision the next query simply makes differently — no re-sync, and the
    /// hidden row's ownership and minutes are exactly as inserted throughout.
    /// </summary>
    [Fact]
    public async Task Hiding_a_row_destroys_nothing_and_needs_no_resync()
    {
        var tool = await SeedAsync("Palworld Dedicated Server", "Tool", playtimeMinutes: 77);
        await SeedAsync("Palworld", "Game");

        Assert.DoesNotContain(await QueryAsync(false), r => r.OwnershipId == tool);

        Assert.NotNull(await _ownerships.GetAsync(tool));
        var record = await _plays.GetLatestAsync(tool);
        Assert.NotNull(record);
        Assert.Equal(77, record.PlaytimeMinutes);

        // One flipped flag, no writes in between.
        Assert.Contains(await QueryAsync(true), r => r.OwnershipId == tool);
    }

    /// <summary>The author's four real tools, all four hidden together.</summary>
    [Fact]
    public async Task Every_measured_tool_in_the_real_library_is_hidden_together()
    {
        await SeedAsync("SteamVR Performance Test", "Tool");
        await SeedAsync("Skyrim Creation Kit", "Tool");
        await SeedAsync("Eco Server", "Tool");
        await SeedAsync("Palworld Dedicated Server", "Tool");
        var game = await SeedAsync("Hollow Knight", "Game");

        Assert.Equal(game, Assert.Single(await QueryAsync(false)).OwnershipId);
        Assert.Equal(5, (await QueryAsync(true)).Count);
    }

    // ── NULL is "not known", never "not a game" ──────────────────────────────

    /// <summary>
    /// Migration 0006's central warning, and the way this feature could do real
    /// damage: most of the library has never been probed, so an unstored type
    /// must always be visible.
    /// </summary>
    [Fact]
    public async Task A_null_type_is_always_visible()
    {
        var unprobed = await SeedAsync("Disco Elysium", appType: null);

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == unprobed);
        Assert.Contains(await QueryAsync(true), r => r.OwnershipId == unprobed);
    }

    /// <summary>
    /// The realistic shape of the library: a handful of typed rows in a sea of
    /// untyped ones. Only the tool moves.
    /// </summary>
    [Fact]
    public async Task A_library_that_is_mostly_untyped_loses_only_its_typed_non_games()
    {
        for (var i = 0; i < 20; i++)
        {
            await SeedAsync($"Untyped Game {i}", appType: null);
        }

        await SeedAsync("Aseprite", "Application");

        Assert.Equal(20, (await QueryAsync(false)).Count);
        Assert.Equal(21, (await QueryAsync(true)).Count);
    }

    /// <summary>An empty or whitespace-only stored type is "not known" too.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_type_is_treated_as_unknown_and_stays_visible(string appType)
    {
        var row = await SeedAsync("Half-Life 2", appType);

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == row);
    }

    // ── Games and demos are never touched by this rule ───────────────────────

    /// <summary>
    /// Casing is Valve's and is not stable — Bastion answers <c>game</c> where
    /// Monster Hunter Wilds answers <c>Game</c> — and <c>Demo</c> belongs to
    /// consolidation, not to this filter. None of the three is ever hidden here.
    /// </summary>
    [Theory]
    [InlineData("Game")]
    [InlineData("game")]
    [InlineData("Demo")]
    [InlineData("demo")]
    public async Task Game_and_demo_types_are_never_filtered(string appType)
    {
        var row = await SeedAsync("Bastion", appType);

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == row);
        Assert.Contains(await QueryAsync(true), r => r.OwnershipId == row);
    }

    /// <summary>
    /// A solitary demo is the only copy of that game the user has, and the
    /// filter must not become a second, blunter reason to hide it.
    /// </summary>
    [Fact]
    public async Task A_solitary_demo_survives_the_filter()
    {
        var demo = await SeedAsync("Hellpoint Demo", "Demo", playtimeMinutes: 30);

        var row = Assert.Single(await QueryAsync(false));
        Assert.Equal(demo, row.OwnershipId);
    }

    // ── Casing, padding, and values nobody has seen yet ──────────────────────

    [Theory]
    [InlineData("Tool")]
    [InlineData("tool")]
    [InlineData("TOOL")]
    [InlineData("  Tool  ")]
    [InlineData("\tMusic\n")]
    [InlineData("APPLICATION")]
    public async Task Casing_and_padding_are_ignored(string appType)
    {
        var nonGame = await SeedAsync("Some Non-Game", appType);
        var game = await SeedAsync("Some Game", "Game");

        var rows = await QueryAsync(false);

        Assert.Equal(game, Assert.Single(rows).OwnershipId);
        Assert.Contains(await QueryAsync(true), r => r.OwnershipId == nonGame);
    }

    /// <summary>
    /// Valve's vocabulary is undocumented and can gain a value at any time.
    /// §5.3's precision-over-recall stance says an unrecognised value is not a
    /// licence to guess: a tool left on screen is clutter, a game hidden from
    /// the user's own library is a lie about what they own.
    /// </summary>
    [Theory]
    [InlineData("Bundle")]
    [InlineData("Franchise")]
    [InlineData("SomethingValveInventsIn2027")]
    [InlineData("DLC")]
    [InlineData("Mod")]
    public async Task An_unknown_future_type_stays_visible(string appType)
    {
        var row = await SeedAsync("Unrecognised", appType);

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == row);
        Assert.False(NonGameEntries.IsNonGame(appType));
    }

    /// <summary>The whole declared set, hidden as one, with nothing else moving.</summary>
    [Fact]
    public async Task Every_declared_non_game_type_is_hidden()
    {
        foreach (var type in NonGameEntries.HiddenTypes)
        {
            await SeedAsync($"Entry typed {type}", type);
        }

        var game = await SeedAsync("A Real Game", "Game");

        Assert.Equal(game, Assert.Single(await QueryAsync(false)).OwnershipId);
        Assert.Equal(
            NonGameEntries.HiddenTypes.Count + 1,
            (await QueryAsync(true)).Count);
    }

    // ── Counts and rows agree ────────────────────────────────────────────────

    /// <summary>
    /// The requirement the rail depends on: the counts the view computes come
    /// from these rows, so a hidden entry is missing from the total AND from
    /// every bucket tally, in both states. An interface that says 606 and shows
    /// 602 contradicts itself.
    /// </summary>
    [Fact]
    public async Task Bucket_counts_and_returned_rows_agree_in_both_states()
    {
        await SeedAsync("Never Played Game", "Game", playtimeMinutes: 10);
        await SeedAsync("Bounced Game", "Game", playtimeMinutes: 300);
        await SeedAsync("Played Out Game", "Game", playtimeMinutes: 9_000);
        await SeedAsync("Eco Server", "Tool", playtimeMinutes: 10);
        await SeedAsync("Soundtrack", "Music", playtimeMinutes: 9_000);

        var hidden = await QueryAsync(false);
        Assert.Equal(3, hidden.Count);
        Assert.Equal(1, hidden.Count(r => r.Bucket == LibraryBuckets.NeverPlayed));
        Assert.Equal(1, hidden.Count(r => r.Bucket == LibraryBuckets.Bounced));
        Assert.Equal(1, hidden.Count(r => r.Bucket == LibraryBuckets.Retired));

        var shown = await QueryAsync(true);
        Assert.Equal(5, shown.Count);
        Assert.Equal(2, shown.Count(r => r.Bucket == LibraryBuckets.NeverPlayed));
        Assert.Equal(1, shown.Count(r => r.Bucket == LibraryBuckets.Bounced));
        Assert.Equal(2, shown.Count(r => r.Bucket == LibraryBuckets.Retired));

        // Whichever state it is in, the bucket tallies sum to the total the
        // rail would print beside "All games".
        foreach (var rows in new[] { hidden, shown })
        {
            Assert.Equal(
                rows.Count,
                rows.Count(r => r.Bucket == LibraryBuckets.NeverPlayed)
                + rows.Count(r => r.Bucket == LibraryBuckets.Bounced)
                + rows.Count(r => r.Bucket == LibraryBuckets.StaleButPatched)
                + rows.Count(r => r.Bucket == LibraryBuckets.Retired)
                + rows.Count(r => r.Bucket == LibraryBuckets.Active));
        }
    }

    /// <summary>
    /// A hidden row keeps its own bucket and its own minutes when it comes
    /// back — the filter drops rows, it does not recompute them.
    /// </summary>
    [Fact]
    public async Task A_shown_non_game_row_is_bucketed_normally()
    {
        var tool = await SeedAsync("SteamVR Performance Test", "Tool", playtimeMinutes: 4);

        var row = Assert.Single(await QueryAsync(true));

        Assert.Equal(tool, row.OwnershipId);
        Assert.Equal(4, row.PlaytimeMinutes);
        Assert.Equal(LibraryBuckets.NeverPlayed, row.Bucket);
    }

    // ── Consolidation is unaffected ──────────────────────────────────────────

    /// <summary>
    /// Requirement 2, asserted directly: the demo/base outcome is byte-identical
    /// with the filter on and off. Consolidation is fed every owned row either
    /// way, so the filter can move a tool off the screen but can never change
    /// which demo is folded into which game.
    /// </summary>
    [Fact]
    public async Task Demo_consolidation_behaves_identically_with_the_filter_on_and_off()
    {
        var full = await SeedAsync("Bastion", "game", playtimeMinutes: 900);
        await SeedAsync("Bastion Demo", "Demo", playtimeMinutes: 42);
        var beta = await SeedAsync("Gatewalkers (Alpha)", "Game", playtimeMinutes: 60);
        await SeedAsync("Skyrim Creation Kit", "Tool");

        var hidden = await QueryAsync(false);
        var shown = await QueryAsync(true);

        // Same consolidation decision in both: the demo is gone, the base
        // absorbed exactly one, and the unbound alpha is a normal row.
        foreach (var rows in new[] { hidden, shown })
        {
            var baseRow = Assert.Single(rows, r => r.OwnershipId == full);
            Assert.Equal(900, baseRow.PlaytimeMinutes);
            Assert.Equal(1, baseRow.ConsolidatedDemoCount);
            Assert.Contains(rows, r => r.OwnershipId == beta);
        }

        // The ONLY difference between the two reads is the tool.
        Assert.Equal(
            shown.Select(r => r.OwnershipId).Where(id => hidden.Any(h => h.OwnershipId == id)),
            hidden.Select(r => r.OwnershipId));
        Assert.Equal(hidden.Count + 1, shown.Count);
    }

    /// <summary>
    /// A demo whose base is owned stays consolidated even when the filter is
    /// on — the two mechanisms compose, and neither double-counts the other.
    /// </summary>
    [Fact]
    public async Task A_consolidated_demo_is_not_resurrected_by_the_filter()
    {
        var full = await SeedAsync("Enshrouded", "Game", playtimeMinutes: 2_000);
        var demo = await SeedAsync("Enshrouded", "Demo", playtimeMinutes: 30);

        var row = Assert.Single(await QueryAsync(false));
        Assert.Equal(full, row.OwnershipId);
        Assert.NotNull(await _ownerships.GetAsync(demo));
    }

    // ── The predicate itself ─────────────────────────────────────────────────

    [Fact]
    public void The_hidden_set_excludes_the_types_that_are_games()
    {
        Assert.False(NonGameEntries.IsNonGame(null));
        Assert.False(NonGameEntries.IsNonGame("Game"));
        Assert.False(NonGameEntries.IsNonGame("game"));
        Assert.False(NonGameEntries.IsNonGame("Demo"));
        Assert.DoesNotContain("demo", NonGameEntries.HiddenTypes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("game", NonGameEntries.HiddenTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_hidden_set_covers_what_was_declared()
    {
        foreach (var type in new[]
                 {
                     "tool", "application", "config", "music",
                     "video", "movie", "episode", "series", "media", "hardware",
                 })
        {
            Assert.True(NonGameEntries.IsNonGame(type), type);
            Assert.True(NonGameEntries.IsNonGame(type.ToUpperInvariant()), type);
        }
    }

    // ── The setting ──────────────────────────────────────────────────────────

    /// <summary>
    /// The preference round-trips through the store the UI will use, under the
    /// <c>module.thing</c> key the dimming toggle established.
    /// </summary>
    [Fact]
    public async Task The_setting_round_trips_and_defaults_to_hidden()
    {
        var settings = new SettingsRepository(_db.Factory);

        Assert.Equal("library.show_non_game_entries", BucketThresholds.ShowNonGameEntriesSettingKey);

        // Never written: the default stands, and it is "hidden".
        Assert.Null(await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey));
        Assert.False(BucketThresholds.ParseShowNonGameEntries(
            await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey)));
        Assert.False(BucketThresholds.Default.ShowNonGameEntries);

        await settings.SetAsync(
            BucketThresholds.ShowNonGameEntriesSettingKey,
            BucketThresholds.FormatShowNonGameEntries(true));

        Assert.True(BucketThresholds.ParseShowNonGameEntries(
            await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey)));

        await settings.SetAsync(
            BucketThresholds.ShowNonGameEntriesSettingKey,
            BucketThresholds.FormatShowNonGameEntries(false));

        Assert.False(BucketThresholds.ParseShowNonGameEntries(
            await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey)));
    }

    /// <summary>Unreadable text falls back to the default, which is hidden.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes please")]
    [InlineData("1")]
    public void An_unreadable_preference_reads_as_hidden(string? stored)
        => Assert.False(BucketThresholds.ParseShowNonGameEntries(stored));

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("  TRUE  ")]
    public void The_stored_true_is_read_case_and_whitespace_insensitively(string stored)
        => Assert.True(BucketThresholds.ParseShowNonGameEntries(stored));

    /// <summary>
    /// The end-to-end shape the UI will use: read the key, parse it, hand it to
    /// the query on the thresholds record. Flipping the setting changes the next
    /// read with nothing else touched.
    /// </summary>
    [Fact]
    public async Task The_stored_preference_drives_the_query()
    {
        var settings = new SettingsRepository(_db.Factory);
        await SeedAsync("Eco", "Game");
        await SeedAsync("Eco Server", "Tool");

        async Task<int> LoadAsync()
        {
            var show = BucketThresholds.ParseShowNonGameEntries(
                await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey));

            var rows = await new LibraryQueryRepository(_db.Factory).GetOwnershipBucketsAsync(
                BucketThresholds.Default with { ShowNonGameEntries = show });

            return rows.Count;
        }

        Assert.Equal(1, await LoadAsync());

        await settings.SetAsync(
            BucketThresholds.ShowNonGameEntriesSettingKey,
            BucketThresholds.FormatShowNonGameEntries(true));

        Assert.Equal(2, await LoadAsync());
    }
}
