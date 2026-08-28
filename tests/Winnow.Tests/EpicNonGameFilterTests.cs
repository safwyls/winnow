using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Epic half of the "hide non-game entries" filter — the same setting, the
/// same place in the read query, and the same rule the local Epic scan applies
/// before a candidate is ever emitted.
///
/// <para><b>The bug these pin.</b> Epic's authenticated library endpoint returns
/// raw entitlements with no categories, so the API half of Epic ingest had
/// nothing to filter on and filtered nothing. On the author's library that put
/// 29 rows in the grid titled <c>App &lt;32 hex&gt;</c>: three real games, three
/// Unreal Engine builds, eighteen engine sample packs, two <c>hidden</c> Fortnite
/// content entitlements and a store DLC. The fix classifies them from Epic's own
/// catalog service and hides the non-games — it does <b>not</b> delete them. Two
/// of the 29 have recorded playtime against them, and the user owns all of them
/// either way.</para>
/// </summary>
public class EpicNonGameFilterTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;

    public EpicNonGameFilterTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<long> SeedAsync(
        string title, string? epicCategories, long playtimeMinutes = 300)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = title,
            EpicCategories = epicCategories,
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
            Store = "epic",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "epic_api",
            ObservedAt = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc),
        });

        return ownershipId;
    }

    private Task<IReadOnlyList<OwnershipBucket>> QueryAsync(bool showNonGameEntries)
        => new LibraryQueryRepository(_db.Factory).GetOwnershipBucketsAsync(
            BucketThresholds.Default with { ShowNonGameEntries = showNonGameEntries });

    // ── The default, and the toggle ──────────────────────────────────────────

    [Fact]
    public async Task An_Unreal_Engine_build_is_hidden_by_default_and_returns_when_the_setting_is_on()
    {
        var game = await SeedAsync("Fez", "public,games,applications");
        var engine = await SeedAsync("Unreal Engine", "engines,engines/ue4");

        var hidden = await QueryAsync(showNonGameEntries: false);
        Assert.Equal(game, Assert.Single(hidden).OwnershipId);

        var shown = await QueryAsync(showNonGameEntries: true);
        Assert.Equal(2, shown.Count);
        Assert.Contains(shown, r => r.OwnershipId == engine);
    }

    [Fact]
    public async Task The_measured_junk_from_the_real_library_is_hidden_together()
    {
        await SeedAsync("Unreal Engine", "engines,engines/ue4");
        await SeedAsync("Unreal Engine Chaos", "engines,engines/ue4,engines/unstable");
        await SeedAsync("Infinity Blade: Effects", "assets,assets/showcasedemos");
        await SeedAsync("Soul: Cave", "asset-format,asset-format/game-engine,type,type/format-item");
        await SeedAsync("Action RPG", "projects,projects/completeprojects");
        await SeedAsync("Fortnite Save the World Content", "hidden");
        await SeedAsync("Civilization VI : Aztec DLC", "addons");

        var game = await SeedAsync("LEGO® Fortnite: Odyssey", "addons,addons/launchable,applications,games,games/experience");

        Assert.Equal(game, Assert.Single(await QueryAsync(false)).OwnershipId);
        Assert.Equal(8, (await QueryAsync(true)).Count);
    }

    /// <summary>
    /// The 320-minute Unreal Engine row, and the whole reason nothing is
    /// deleted: the user really did spend that time, and hiding it must not cost
    /// the record of it.
    /// </summary>
    [Fact]
    public async Task Hiding_an_owned_non_game_destroys_neither_the_ownership_nor_its_playtime()
    {
        var engine = await SeedAsync("Unreal Engine", "engines,engines/ue4", playtimeMinutes: 320);
        await SeedAsync("Fez", "public,games,applications");

        Assert.DoesNotContain(await QueryAsync(false), r => r.OwnershipId == engine);

        Assert.NotNull(await _ownerships.GetAsync(engine));
        var record = await _plays.GetLatestAsync(engine);
        Assert.NotNull(record);
        Assert.Equal(320, record.PlaytimeMinutes);

        // One flipped flag, no writes in between.
        Assert.Contains(await QueryAsync(true), r => r.OwnershipId == engine);
    }

    // ── NULL is "not known", never "not a game" ──────────────────────────────

    [Fact]
    public async Task An_Epic_work_with_no_stored_categories_stays_visible()
    {
        // Every Epic work named from catcache.bin before migration 0009, which
        // on a real library is most of them. Hiding these would empty the store.
        var unclassified = await SeedAsync("Celeste", null);

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == unclassified);
    }

    [Fact]
    public async Task A_blank_categories_value_stays_visible()
    {
        var blank = await SeedAsync("Alan Wake", "   ");
        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == blank);
    }

    /// <summary>
    /// A DLC that carries <c>games</c> + <c>applications</c> is admitted, and
    /// deliberately. LEGO Fortnite: Odyssey is one, and the user has played it
    /// for 408 minutes; a filter that hid DLC would have taken it off the screen.
    /// </summary>
    [Fact]
    public async Task A_game_shaped_add_on_is_not_treated_as_a_non_game()
    {
        var addon = await SeedAsync("Borderlands 3 Bounty of Blood", "application,games,applications");

        Assert.Contains(await QueryAsync(false), r => r.OwnershipId == addon);
    }

    // ── One notion, two stores ───────────────────────────────────────────────

    [Fact]
    public async Task Steam_and_Epic_are_governed_by_the_same_single_setting()
    {
        var steamTool = await SeedSteamAsync("Skyrim Creation Kit", "Tool");
        var epicEngine = await SeedAsync("Unreal Engine", "engines,engines/ue4");
        var game = await SeedAsync("Fez", "public,games,applications");

        Assert.Equal(game, Assert.Single(await QueryAsync(false)).OwnershipId);

        var shown = await QueryAsync(true);
        Assert.Equal(3, shown.Count);
        Assert.Contains(shown, r => r.OwnershipId == steamTool);
        Assert.Contains(shown, r => r.OwnershipId == epicEngine);
    }

    [Fact]
    public void The_predicate_defers_to_the_one_Epic_rule()
    {
        // Not a restatement of "games + applications" — the query layer asks
        // EpicGameFilter, the same predicate EpicLibrarySource uses to decide
        // what never becomes a candidate.
        foreach (var value in new[]
                 {
                     "public,games,applications",
                     "engines,engines/ue4",
                     "assets,assets/showcasedemos",
                     "hidden",
                     "addons",
                 })
        {
            Assert.Equal(
                EpicGameFilter.IsGame(value) == false,
                NonGameEntries.IsNonGameEpicCategories(value));
        }
    }

    [Fact]
    public void A_source_that_said_nothing_cannot_overrule_one_that_spoke()
    {
        // A Steam type of Game beside no Epic categories is visible; a Steam
        // Tool beside no Epic categories is hidden. The disjunction must never
        // let a null vote.
        Assert.False(NonGameEntries.IsNonGame("Game", null));
        Assert.True(NonGameEntries.IsNonGame("Tool", null));
        Assert.False(NonGameEntries.IsNonGame(null, "public,games,applications"));
        Assert.True(NonGameEntries.IsNonGame(null, "engines,engines/ue4"));
        Assert.False(NonGameEntries.IsNonGame(null, null));
    }

    private async Task<long> SeedSteamAsync(string title, string? appType)
    {
        var workId = await _works.InsertAsync(new Work { Name = title, SteamAppType = appType });
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
            PlaytimeMinutes = 300,
            LastPlayedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "steam_local",
            ObservedAt = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc),
        });

        return ownershipId;
    }
}
