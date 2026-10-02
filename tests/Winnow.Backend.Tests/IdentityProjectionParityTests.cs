using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>
/// Frozen identity fixtures through the authenticated API consumed by Electron.
/// Renderer tests own section visibility, formatted hours and store badges; these
/// tests preserve their actual SQLite-backed facts and exercise link commands over HTTP.
/// </summary>
public sealed class IdentityProjectionParityTests
{
    private static readonly DateTime IdentityNow = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpansionNow = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Civilization = "Sid Meier's Civilization IV";
    private const string BeyondTheSword = "Sid Meier's Civilization IV: Beyond the Sword";

    [Fact]
    public async Task Expansion_link_preserves_every_library_count_bucket_store_and_play_figure_over_HTTP()
    {
        await using var fixture = await Fixture.Create(expansions: true);
        var civ = await fixture.Seed(Civilization, 12_000, ExpansionNow.AddDays(-900));
        var pack = await fixture.Seed(BeyondTheSword, 0, null, year: 2007);
        var before = await fixture.Api.GetLibraryAsync();
        var beforeWorkspace = await fixture.Api.GetWorkspaceAsync();

        await fixture.Group(civ, pack);

        var after = await fixture.Api.GetLibraryAsync();
        var workspace = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(LibraryFigures(before), LibraryFigures(after));
        Assert.Equal(RowFigures(beforeWorkspace), RowFigures(workspace));
        Assert.Equal(2, after.Games.Count);
        Assert.Equal(2, workspace.Buckets.Select(row => row.ResolvedWorkId).Distinct().Count());
        Assert.Equal(2, StoreCounts(after)["steam"]);
        var expansion = Assert.Single(after.Games, game => game.WorkId == pack.WorkId);
        Assert.Equal(0, expansion.PlaytimeMinutes);
        Assert.Equal(LibraryBuckets.NeverPlayed, expansion.Bucket);
        Assert.Null(expansion.LastPlayedAt);
        Assert.Equal(12_000, Assert.Single(after.Games, game => game.WorkId == civ.WorkId).PlaytimeMinutes);
        Assert.All(workspace.Buckets, row => Assert.Equal(row.WorkId, row.ResolvedWorkId));
        var link = Assert.Single(workspace.IdentityLinks);
        Assert.Equal((civ.WorkId, pack.WorkId, IdentityLinkKinds.ExpansionOf),
            (link.ParentWorkId, link.ChildWorkId, link.Kind));
    }

    [Fact]
    public async Task Expansion_minutes_never_enter_the_base_game_or_its_detail_ownerships_over_HTTP()
    {
        await using var fixture = await Fixture.Create(expansions: true);
        var civ = await fixture.Seed(Civilization, 12_000, ExpansionNow.AddDays(-900));
        var pack = await fixture.Seed(BeyondTheSword, 4_000, ExpansionNow.AddDays(-10), year: 2007);
        await fixture.Group(civ, pack);

        var game = await fixture.Api.GetGameAsync(civ.WorkId);
        Assert.Equal(12_000, game.PlaytimeMinutes);
        Assert.Equal(civ.WorkId, Assert.Single(game.Entries).WorkId);
        var details = await fixture.Details(civ);
        Assert.Equal(civ.OwnershipId, Assert.Single(details.Ownerships).Id);
        var expansion = await fixture.Api.GetGameAsync(pack.WorkId);
        Assert.Equal(4_000, expansion.PlaytimeMinutes);
        Assert.Equal(ExpansionNow.AddDays(-10), expansion.LastPlayedAt);
        Assert.Equal(pack.OwnershipId, Assert.Single(expansion.Entries).OwnershipId);
        var link = Assert.Single((await fixture.Api.GetWorkspaceAsync()).IdentityLinks);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, link.Kind);
        Assert.Equal(pack.WorkId, link.ChildWorkId);
    }

    [Fact]
    public async Task Coverage_and_expansion_relations_stay_separate_and_the_pack_names_its_base_over_HTTP()
    {
        await using var fixture = await Fixture.Create(expansions: true);
        var civ = await fixture.Seed(Civilization, 12_000, ExpansionNow.AddDays(-900));
        var gog = await fixture.Seed(Civilization, 60, ExpansionNow.AddDays(-5), store: "gog");
        var pack = await fixture.Seed(BeyondTheSword, 0, null, year: 2007);
        await fixture.Link(civ, gog);
        await fixture.Group(civ, pack);

        var workspace = await fixture.Api.GetWorkspaceAsync();
        var coverage = Assert.Single(workspace.IdentityLinks, link => link.Kind == IdentityLinkKinds.SameGame);
        var expansion = Assert.Single(workspace.IdentityLinks, link => link.Kind == IdentityLinkKinds.ExpansionOf);
        Assert.Equal((civ.WorkId, gog.WorkId), (coverage.ParentWorkId, coverage.ChildWorkId));
        Assert.Equal((civ.WorkId, pack.WorkId), (expansion.ParentWorkId, expansion.ChildWorkId));
        Assert.Equal(Civilization, Assert.Single(workspace.Works, work => work.Id == expansion.ParentWorkId).Name);
        Assert.DoesNotContain(workspace.IdentityLinks, link => link.ParentWorkId == pack.WorkId);
        Assert.DoesNotContain(workspace.IdentityLinks, link => link.ChildWorkId == civ.WorkId);
        var baseGame = await fixture.Api.GetGameAsync(civ.WorkId);
        Assert.Equal(12_060, baseGame.PlaytimeMinutes);
        Assert.Equal(new[] { civ.WorkId, gog.WorkId }, baseGame.Entries.Select(entry => entry.WorkId));
        Assert.Equal(new[] { civ.OwnershipId, gog.OwnershipId },
            (await fixture.Details(civ)).Ownerships.Select(ownership => ownership.Id));
        Assert.Equal(pack.OwnershipId, Assert.Single((await fixture.Details(pack)).Ownerships).Id);
        Assert.Equal(pack.WorkId, Assert.Single((await fixture.Api.GetGameAsync(pack.WorkId)).Entries).WorkId);
    }

    [Fact]
    public async Task Reparenting_a_base_keeps_its_expansion_kind_and_independent_minutes_over_HTTP()
    {
        await using var fixture = await Fixture.Create(expansions: true);
        var steam = await fixture.Seed(Civilization, 12_000, ExpansionNow.AddDays(-900));
        var gog = await fixture.Seed(Civilization, 60, ExpansionNow.AddDays(-5), store: "gog");
        var pack = await fixture.Seed(BeyondTheSword, 4_000, ExpansionNow.AddDays(-10), year: 2007);
        await fixture.Group(steam, pack);
        await fixture.Link(gog, steam);

        var workspace = await fixture.Api.GetWorkspaceAsync();
        var expansion = Assert.Single(workspace.IdentityLinks, link => link.ChildWorkId == pack.WorkId);
        Assert.Equal(gog.WorkId, expansion.ParentWorkId);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, expansion.Kind);
        var sameGame = Assert.Single(workspace.IdentityLinks, link => link.Kind == IdentityLinkKinds.SameGame);
        Assert.Equal((gog.WorkId, steam.WorkId), (sameGame.ParentWorkId, sameGame.ChildWorkId));
        Assert.Equal(pack.WorkId, Assert.Single(workspace.Buckets, row => row.WorkId == pack.WorkId).ResolvedWorkId);
        var games = (await fixture.Api.GetLibraryAsync()).Games;
        Assert.Equal(2, games.Count);
        Assert.Equal(12_060, Assert.Single(games, game => game.WorkId == gog.WorkId).PlaytimeMinutes);
        Assert.Equal(4_000, Assert.Single(games, game => game.WorkId == pack.WorkId).PlaytimeMinutes);
    }

    [Fact]
    public async Task Same_game_link_collapses_one_game_without_changing_store_counts_or_original_rows_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30));
        var epic = await fixture.Seed("Prey", 90, IdentityNow.AddDays(-400), store: "epic");
        await fixture.Seed("Dishonored", 0, null);
        var before = await fixture.Api.GetLibraryAsync();
        var beforeRows = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(3, before.Games.Count);

        await fixture.Link(steam, epic);

        var after = await fixture.Api.GetLibraryAsync();
        var afterRows = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(RowFigures(beforeRows), RowFigures(afterRows));
        Assert.Equal(3, afterRows.Buckets.Count);
        Assert.Equal(2, after.Games.Count);
        Assert.Equal(2, afterRows.Buckets.Select(row => row.ResolvedWorkId).Distinct().Count());
        Assert.Equal(StoreCounts(before), StoreCounts(after));
        Assert.Equal(2, StoreCounts(after)["steam"]);
        Assert.Equal(1, StoreCounts(after)["epic"]);
        Assert.Equal(3, StoreCounts(after).Values.Sum());
    }

    [Fact]
    public async Task Linked_pair_uses_primary_title_and_art_without_crossing_each_copys_figures_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        // Distinct URLs supplement the source's distinct Steam cover keys so the HTTP
        // artwork assertion cannot pass by comparing two absent URLs.
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30), cover: "https://fixtures.invalid/prey-steam.jpg");
        var epic = await fixture.Seed("Prey (2017)", 90, IdentityNow.AddDays(-40), store: "epic", cover: "https://fixtures.invalid/prey-epic.jpg");
        var before = (await fixture.Api.GetLibraryAsync()).Games;
        Assert.Equal(new[] { "Prey", "Prey (2017)" }, before.Select(game => game.Title));
        Assert.NotEqual(before[0].CoverUrl, before[1].CoverUrl);
        var workspaceBefore = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(new[] { "500001", "500002" }, workspaceBefore.ExternalIds.Select(id => id.ProviderId));

        await fixture.Link(steam, epic);

        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal("Prey", game.Title);
        Assert.Equal(before[0].CoverUrl, game.CoverUrl);
        Assert.Equal(new[] { "steam", "epic" }, game.Entries.Select(entry => entry.Store));
        Assert.Equal(390, game.PlaytimeMinutes);
        Assert.Equal(IdentityNow.AddDays(-30), game.LastPlayedAt);
        Assert.Equal(300, Assert.Single(game.Entries, entry => entry.Store == "steam").PlaytimeMinutes);
        var epicEntry = Assert.Single(game.Entries, entry => entry.Store == "epic");
        Assert.Equal(90, epicEntry.PlaytimeMinutes);
        Assert.Equal(IdentityNow.AddDays(-40), epicEntry.LastPlayedAt);
        var workspaceAfter = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(workspaceBefore.ExternalIds, workspaceAfter.ExternalIds);
        Assert.Equal(workspaceBefore.Works, workspaceAfter.Works);
    }

    [Fact]
    public async Task Covered_titles_keep_their_own_store_minutes_and_dates_in_the_detail_scope_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30));
        var epic = await fixture.Seed("Prey Deluxe", 90, IdentityNow.AddDays(-400), store: "epic");
        await fixture.Link(steam, epic);

        var game = await fixture.Api.GetGameAsync(steam.WorkId);
        Assert.Equal(2, game.Entries.Count);
        var own = Assert.Single(game.Entries, entry => entry.WorkId == game.WorkId);
        Assert.Equal(("Prey", "steam", 300L, IdentityNow.AddDays(-30)),
            (own.Title, own.Store, own.PlaytimeMinutes, own.LastPlayedAt));
        var covered = Assert.Single(game.Entries, entry => entry.WorkId != game.WorkId);
        Assert.Equal(("Prey Deluxe", "epic", 90L, IdentityNow.AddDays(-400)),
            (covered.Title, covered.Store, covered.PlaytimeMinutes, covered.LastPlayedAt));
        Assert.Equal(new[] { steam.OwnershipId, epic.OwnershipId },
            (await fixture.Details(steam)).Ownerships.Select(ownership => ownership.Id));
    }

    [Fact]
    public async Task Detail_achievement_rows_keep_ten_of_ten_and_three_of_ten_without_a_blended_percentage_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30));
        var epic = await fixture.Seed("Prey", 90, IdentityNow.AddDays(-40), store: "epic");
        await fixture.Link(steam, epic);
        await fixture.SeedAchievements(steam, total: 10, unlocked: 10);
        await fixture.SeedAchievements(epic, total: 10, unlocked: 3);

        var details = await fixture.Details(steam);
        Assert.Equal(2, details.Achievements.Count);
        var steamProgress = Assert.Single(details.Achievements, row => row.ReleaseId == steam.ReleaseId);
        var epicProgress = Assert.Single(details.Achievements, row => row.ReleaseId == epic.ReleaseId);
        Assert.Equal((10, 10, 100d), (steamProgress.Unlocked, steamProgress.Total, steamProgress.PercentComplete));
        Assert.Equal((3, 10, 30d), (epicProgress.Unlocked, epicProgress.Total, epicProgress.PercentComplete));
        Assert.All(details.Achievements, row =>
        {
            Assert.True(row.HasKnownProgress);
            Assert.Equal("12345", row.AccountRef);
            Assert.NotEqual(65d, row.PercentComplete);
        });
    }

    [Fact]
    public async Task Unsupported_copy_keeps_unknown_progress_distinct_from_known_zero_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30));
        var epic = await fixture.Seed("Prey", 90, IdentityNow.AddDays(-40), store: "epic");
        await fixture.Link(steam, epic);
        await fixture.SeedAchievements(steam, total: 4, unlocked: 1);

        var details = await fixture.Details(steam);
        var known = Assert.Single(details.Achievements, row => row.ReleaseId == steam.ReleaseId);
        Assert.Equal((1, 4, 25d), (known.Unlocked, known.Total, known.PercentComplete));
        var unknown = Assert.Single(details.Achievements, row => row.ReleaseId == epic.ReleaseId);
        Assert.Equal(AchievementAvailability.Unknown, unknown.Availability);
        Assert.False(unknown.HasKnownProgress);
        Assert.False(unknown.HasAny);
        Assert.Null(unknown.PercentComplete);
        Assert.Equal("epic", Assert.Single(details.Ownerships, ownership => ownership.ReleaseId == unknown.ReleaseId).Store);
    }

    [Fact]
    public async Task Standalone_game_has_no_covered_identity_or_extra_detail_ownership_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var dishonored = await fixture.Seed("Dishonored", 40, IdentityNow.AddDays(-9));
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(dishonored.WorkId, Assert.Single(game.Entries).WorkId);
        Assert.Equal(dishonored.OwnershipId, Assert.Single((await fixture.Details(dishonored)).Ownerships).Id);
        Assert.Empty((await fixture.Api.GetWorkspaceAsync()).IdentityLinks);
    }

    [Fact]
    public async Task Separating_one_covered_copy_keeps_its_sibling_in_the_same_act_and_restores_its_tile_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, IdentityNow.AddDays(-30));
        var epic = await fixture.Seed("Prey Epic", 90, IdentityNow.AddDays(-40), store: "epic");
        var gog = await fixture.Seed("Prey GOG", 20, IdentityNow.AddDays(-50), store: "gog");
        var act = await fixture.Link(steam, epic, gog);
        var before = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal(2, before.IdentityLinks.Count);
        Assert.All(before.IdentityLinks, link => Assert.Equal(act.ActId, link.ActId));
        var epicLink = Assert.Single(before.IdentityLinks, link => link.ChildWorkId == epic.WorkId);

        await fixture.Api.SeparateGameAsync(epic.WorkId, new(epicLink.Id));

        var after = await fixture.Api.GetWorkspaceAsync();
        var remaining = Assert.Single(after.IdentityLinks);
        Assert.Equal((steam.WorkId, gog.WorkId, act.ActId),
            (remaining.ParentWorkId, remaining.ChildWorkId, remaining.ActId));
        Assert.Equal(epic.WorkId, Assert.Single(after.Buckets, row => row.WorkId == epic.WorkId).ResolvedWorkId);
        Assert.Equal(steam.WorkId, Assert.Single(after.Buckets, row => row.WorkId == gog.WorkId).ResolvedWorkId);
        Assert.Equal(RowFigures(before), RowFigures(after));
        Assert.Equal(before.Works, after.Works);
        Assert.Equal(before.Releases, after.Releases);
        Assert.Equal(before.Ownerships, after.Ownerships);
        var games = (await fixture.Api.GetLibraryAsync()).Games;
        Assert.Equal(2, games.Count);
        Assert.Equal("Prey Epic", Assert.Single(games, game => game.WorkId == epic.WorkId).Title);
        var group = Assert.Single(games, game => game.WorkId == steam.WorkId);
        Assert.Equal(gog.WorkId, Assert.Single(group.Entries, entry => entry.WorkId != steam.WorkId).WorkId);
        Assert.Equal(new[] { steam.OwnershipId, gog.OwnershipId },
            (await fixture.Details(steam)).Ownerships.Select(ownership => ownership.Id));
    }

    private static SortedDictionary<string, int> StoreCounts(LibraryResponse library)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var store in library.Games.SelectMany(game => game.Entries.Select(entry => entry.Store).Distinct()))
            counts[store] = counts.GetValueOrDefault(store) + 1;
        return counts;
    }

    private static string LibraryFigures(LibraryResponse library) => JsonSerializer.Serialize(new
    {
        Total = library.Games.Count,
        Stores = StoreCounts(library),
        Buckets = library.Games.GroupBy(game => game.Bucket).OrderBy(group => group.Key)
            .Select(group => new { Bucket = group.Key, Count = group.Count() }),
        Games = library.Games.Select(game => new { game.WorkId, game.Title, game.PlaytimeMinutes, game.Bucket, game.LastPlayedAt }),
    });

    private static string RowFigures(LibraryWorkspaceResponse workspace) => JsonSerializer.Serialize(
        workspace.Buckets.Select(row => new
        {
            row.OwnershipId, row.ReleaseId, row.WorkId, row.Bucket, row.PlaytimeMinutes, row.LastPlayedAt,
        }));

    private sealed record Entry(long WorkId, long ReleaseId, long OwnershipId);

    private sealed class Fixture(WebApplication app, string directory, bool expansions) : IAsyncDisposable
    {
        private int _appId = expansions ? 800_000 : 500_000;
        private DateTime Now => expansions ? ExpansionNow : IdentityNow;
        private IServiceProvider Services => app.Services;
        public WinnowApiClient Api { get; } = WinnowApiClient.Attach(directory);

        public static async Task<Fixture> Create(bool expansions = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-identity-projection-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            try
            {
                await app.StartAsync();
                return new(app, directory, expansions);
            }
            catch
            {
                await app.DisposeAsync();
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public async Task<Entry> Seed(string title, long minutes, DateTime? lastPlayed,
            string store = "steam", int? year = null, string? cover = null)
        {
            var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work
            {
                Name = title, FirstReleaseYear = year ?? (expansions ? 2005 : 2017),
                Publisher = expansions ? "2K Games" : null, CoverUrl = cover,
            });
            var releases = Services.GetRequiredService<IReleaseRepository>();
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = title, Platform = "windows" });
            await releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = release, Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });
            var ownership = await Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership
            {
                ReleaseId = release, Store = store,
            });
            await Services.GetRequiredService<IPlayRecordRepository>().InsertAsync(new PlayRecord
            {
                OwnershipId = ownership, PlaytimeMinutes = minutes, LastPlayedAt = lastPlayed,
                Source = "steam_localconfig", ObservedAt = Now,
            });
            return new(work, release, ownership);
        }

        public Task<IdentityActResponse> Link(Entry parent, params Entry[] children)
            => LinkAs(parent, children, IdentityLinkKinds.SameGame);

        public Task<IdentityActResponse> Group(Entry parent, Entry child)
            => LinkAs(parent, [child], IdentityLinkKinds.ExpansionOf);

        private Task<IdentityActResponse> LinkAs(Entry parent, Entry[] children, string kind)
            => Api.LinkGamesAsync(new(parent.WorkId, children.Select(child => child.WorkId).ToArray(),
                children.Append(parent).ToDictionary(entry => entry.WorkId, entry => entry.WorkId), kind));

        public Task<GameDetailsResponse> Details(Entry entry)
            => Api.GetAsync<GameDetailsResponse>($"games/{entry.WorkId}/details");

        public async Task SeedAchievements(Entry entry, int total, int unlocked)
        {
            await Services.GetRequiredService<ISettingsRepository>().SetAsync(SteamOwnedAccount.RefSettingKey, "12345");
            using var lease = Services.GetRequiredService<ISqliteConnectionFactory>().Lease();
            var releaseId = entry.ReleaseId;
            await lease.Connection.ExecuteAsync("""
                INSERT INTO achievement_observations(release_id,account_ref,availability,attempted_at,schema_at,progress_at)
                VALUES(@releaseId,'12345',3,@at,@at,@at);
                """, new { releaseId, at = DateTime.UtcNow }, lease.Transaction);
            for (var index = 0; index < total; index++)
            {
                var key = $"ach_{releaseId}_{index}";
                await lease.Connection.ExecuteAsync("""
                    INSERT INTO achievements(release_id,provider_key,name,description,hidden)
                    VALUES(@releaseId,@key,@name,NULL,0);
                    """, new { releaseId, key, name = $"Achievement {index}" }, lease.Transaction);
                if (index < unlocked)
                    await lease.Connection.ExecuteAsync("""
                        INSERT INTO account_achievement_unlocks(release_id,provider_key,account_ref,unlocked_at)
                        VALUES(@releaseId,@key,'12345',@at);
                        """, new { releaseId, key, at = Now.AddDays(-10) }, lease.Transaction);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
