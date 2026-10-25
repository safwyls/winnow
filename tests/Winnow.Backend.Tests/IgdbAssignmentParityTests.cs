using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>The frozen Prey fixtures through real assignment, pin, artwork and identity HTTP operations.</summary>
public sealed class IgdbAssignmentParityTests
{
    private const string WrongCover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg";
    private const string RightCover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg";
    private static readonly DateTime Observed = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastPlayed = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("gog", true)]
    [InlineData("steam", true)]
    [InlineData("steam", false)]
    public async Task Assigning_and_clearing_Prey_rewrites_metadata_and_pin_art_without_changing_owned_identity_or_play_facts_over_HTTP(string store, bool hasCover)
    {
        await using var host = await Host.Start(hasCover: hasCover);
        var entry = await host.Seed(store: store);
        var before = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(("Prey", 2006, "2K Games", "A Cherokee garage mechanic is abducted.", WrongCover),
            (before.Title, before.FirstReleaseYear, before.Publisher, before.Summary, before.CoverUrl));
        var originalArt = store == "steam" ? CoverKey.Steam("3900") : CoverKey.Igdb("co1r76");
        Assert.Equal(originalArt, await host.Cover(entry.WorkId));
        var candidates = await host.Details.SearchIgdbAsync("Prey");
        var candidate = Assert.Single(candidates);
        Assert.Equal((5678, "Prey", 2017, hasCover ? RightCover : null),
            (candidate.IgdbId, candidate.Name, candidate.FirstReleaseYear, candidate.CoverUrl));
        Assert.Equal(new[] { "PC (Microsoft Windows)", "PlayStation 4" }, candidate.Platforms);
        Assert.Equal("Prey", Assert.Single(host.Provider.Searches));
        var direct = await host.Details.GetIgdbCandidateAsync(5678);
        Assert.Equal(candidate.IgdbId, direct.IgdbId);
        Assert.Equal(candidate.Platforms, direct.Platforms);

        Assert.Equal("Assigned", (await host.Assign(entry.WorkId, 5678)).Outcome);
        using var reopened = host.Reopen();
        var after = Assert.Single((await reopened.GetLibraryAsync()).Games);
        Assert.Equal(("Prey", 2017, "Bethesda Softworks", "Morgan Yu wakes on Talos I.", hasCover ? RightCover : null),
            (after.Title, after.FirstReleaseYear, after.Publisher, after.Summary, after.CoverUrl));
        Assert.Equal(before.WorkId, after.WorkId);
        Assert.Equal(before.Entries, after.Entries);
        Assert.Equal((120L, LastPlayed), (after.PlaytimeMinutes, after.LastPlayedAt));
        var details = new DetailsClient(reopened);
        var pinned = await details.GetIgdbStateAsync(entry.WorkId);
        Assert.True(pinned.Available);
        Assert.Equal(5678, pinned.Pin?.IgdbId);
        Assert.True((await details.GetMetadataAsync(entry.WorkId)).IsPinned);
        Assert.Contains(entry.WorkId, (await reopened.GetWorkspaceAsync()).PinnedWorkIds);
        Assert.Equal(hasCover ? CoverKey.Igdb("co2abc") : originalArt, await host.Cover(entry.WorkId));
        if (store == "steam")
            Assert.Contains((await reopened.GetWorkspaceAsync()).ExternalIds,
                id => id.ReleaseId == entry.ReleaseId && id.Provider == "steam" && id.ProviderId == "3900");

        Assert.True(await details.ClearIgdbAsync(entry.WorkId, new(pinned.Revision)));
        var cleared = await details.GetIgdbStateAsync(entry.WorkId);
        Assert.Null(cleared.Pin);
        Assert.False((await details.GetMetadataAsync(entry.WorkId)).IsPinned);
        Assert.DoesNotContain(entry.WorkId, (await reopened.GetWorkspaceAsync()).PinnedWorkIds);
        Assert.Equal(store == "steam" ? originalArt : CoverKey.Igdb("co2abc"), await host.Cover(entry.WorkId));
        var final = Assert.Single((await reopened.GetLibraryAsync()).Games);
        Assert.Equivalent(after, final, strict: true);
        Assert.Null(await host.Services.GetRequiredService<IWorkIgdbPinRepository>().GetAsync(entry.WorkId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_claimed_Prey_entry_changes_nothing_until_the_user_accepts_a_same_game_link_over_HTTP(bool accept)
    {
        await using var host = await Host.Start();
        var holder = await host.Seed(igdbId: 5678, cover: RightCover, year: 2017);
        var wrong = await host.Seed(store: "steam");
        var before = await host.Api.GetWorkspaceAsync();
        Assert.Equal("IgdbIdClaimedByAnotherWork", (await host.Assign(wrong.WorkId, 5678)).Outcome);
        var claim = await host.Details.GetIgdbClaimingGameAsync(5678);
        Assert.Equal((holder.WorkId, "Prey", 2017, RightCover),
            (claim.WorkId, claim.Title, claim.FirstReleaseYear, claim.CoverUrl));
        Assert.Equal(2006, (await host.Api.GetLibraryAsync()).Games.Single(game => game.WorkId == wrong.WorkId).FirstReleaseYear);
        Assert.Null((await host.Details.GetIgdbStateAsync(wrong.WorkId)).Pin);
        Assert.Equal(before.Works, (await host.Api.GetWorkspaceAsync()).Works);

        if (accept)
        {
            await host.Api.LinkGamesAsync(new(holder.WorkId, [wrong.WorkId],
                new Dictionary<long, long> { [holder.WorkId] = holder.WorkId, [wrong.WorkId] = wrong.WorkId }));
            var link = Assert.Single(await host.Services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync(), row => row.IsLive);
            Assert.Equal((holder.WorkId, wrong.WorkId, IdentityLinkKinds.SameGame, IdentityLinkSources.User),
                (link.ParentWorkId, link.ChildWorkId, link.Kind, link.Source));
            var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
            Assert.Equal(holder.WorkId, game.WorkId);
            Assert.Equal(new[] { holder.OwnershipId, wrong.OwnershipId }.Order(), game.Entries.Select(entry => entry.OwnershipId).Order());
        }
        else
        {
            // Decline is a renderer-only dismissal: the server receives no follow-up mutation.
            Assert.Empty(await host.Services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync());
            Assert.Equal(before.Works, (await host.Api.GetWorkspaceAsync()).Works);
            Assert.Equal(2, (await host.Api.GetLibraryAsync()).Games.Count);
            Assert.Equal(1234, (await host.Services.GetRequiredService<IWorkRepository>().GetAsync(wrong.WorkId))!.IgdbId);
            Assert.Equal(5678, (await host.Services.GetRequiredService<IWorkRepository>().GetAsync(holder.WorkId))!.IgdbId);
        }
        Assert.Null((await host.Details.GetIgdbStateAsync(wrong.WorkId)).Pin);
        Assert.Null((await host.Details.GetIgdbStateAsync(holder.WorkId)).Pin);
    }

    [Fact]
    public async Task A_structurally_refused_claim_returns_a_conflict_without_writing_a_link_or_pin_over_HTTP()
    {
        await using var host = await Host.Start();
        var holder = await host.Seed(igdbId: 5678, cover: RightCover, year: 2017);
        var wrong = await host.Seed(store: "steam");
        Assert.Equal("IgdbIdClaimedByAnotherWork", (await host.Assign(wrong.WorkId, 5678)).Outcome);
        // The source injects ParentIsAlreadyAChild. Create that real structural condition instead.
        var parent = await host.Seed(title: "Parent", igdbId: null);
        await host.Api.LinkGamesAsync(new(parent.WorkId, [holder.WorkId],
            new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [holder.WorkId] = holder.WorkId }));
        var links = host.Services.GetRequiredService<IIdentityLinkRepository>();
        var prior = await links.GetHistoryAsync();
        var problem = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.LinkGamesAsync(new(holder.WorkId,
            [wrong.WorkId], new Dictionary<long, long> { [holder.WorkId] = parent.WorkId, [wrong.WorkId] = wrong.WorkId })));
        Assert.Equal(HttpStatusCode.Conflict, problem.StatusCode);
        using var json = JsonDocument.Parse(problem.ResponseBody);
        Assert.Equal("ParentIsAlreadyAChild", json.RootElement.GetProperty("refusal").GetString());
        Assert.Equal(prior, await links.GetHistoryAsync());
        Assert.Null((await host.Details.GetIgdbStateAsync(wrong.WorkId)).Pin);
        Assert.Null((await host.Details.GetIgdbStateAsync(holder.WorkId)).Pin);
        Assert.Equal(holder.WorkId, (await host.Details.GetIgdbClaimingGameAsync(5678)).WorkId);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Assignment_availability_reports_the_service_seam_independently_of_credentials_over_HTTP(bool service, bool configured)
    {
        await using var host = await Host.Start(service: service, configured: configured);
        var entry = await host.Seed();
        var state = await host.Details.GetIgdbStateAsync(entry.WorkId);
        Assert.Equal(service, state.Available);
        Assert.Null(state.Pin);
        Assert.Equal(state.Revision, (await host.Details.GetIgdbStateAsync(entry.WorkId)).Revision);
        var wire = await host.Api.GetAsync<JsonElement>($"games/{entry.WorkId}/igdb/state");
        Assert.Equal(service, wire.GetProperty("available").GetBoolean());
        Assert.Equal("Prey", Assert.Single((await host.Api.GetLibraryAsync()).Games).Title);
        if (!service)
        {
            Assert.Empty(await host.Details.SearchIgdbAsync("Prey"));
            Assert.Equal("MetadataUnavailable", (await host.Assign(entry.WorkId, 5678)).Outcome);
            Assert.Equal(1234, (await host.Services.GetRequiredService<IWorkRepository>().GetAsync(entry.WorkId))!.IgdbId);
        }
    }

    [Fact]
    public async Task Correcting_mapping_111_to_222_retires_old_visibility_facets_images_ratings_and_background_over_HTTP()
    {
        await using var host = await Host.Start();
        var entry = await host.Api.CreateManualGameAsync(new("Old game", IgdbId: 111));
        await host.Services.GetRequiredService<IFacetRepository>().SetWorkFacetsAsync(entry.WorkId, [new(FacetKinds.Genre, "Old genre")]);
        await host.Services.GetRequiredService<IWorkMaturityRepository>().UpsertAsync(new()
            { WorkId = entry.WorkId, Source = "igdb", Ratings = "esrb:ao", ObservedAt = Observed });
        await host.Services.GetRequiredService<IWorkImageRepository>().UpsertAsync(new()
            { WorkId = entry.WorkId, Source = "igdb", Kind = ImageKinds.Screenshot, ImageIds = "oldshot", ObservedAt = Observed });
        await host.Services.GetRequiredService<IWorkRatingRepository>().UpsertAsync(new()
            { WorkId = entry.WorkId, Source = RatingSources.IgdbUsers, Score = 95, RatingCount = 20, ObservedAt = Observed });
        using (var connection = host.Services.GetRequiredService<ISqliteConnectionFactory>().Open())
            await connection.ExecuteAsync("""
                UPDATE works SET background_url='https://example.test/old.jpg' WHERE id=@workId;
                INSERT INTO work_field_sources(work_id,field,source,set_at) VALUES(@workId,'background_url','igdb','2026-09-11');
                """, new { workId = entry.WorkId });
        var visible = new LibraryPreferences(false, true, "adults_only");
        var before = await host.Details.GetAsync(entry.WorkId, visible);
        Assert.Equal("oldshot", Assert.Single(before.Images).ImageIds);
        Assert.Equal(95, Assert.Single(before.Ratings).Score);
        Assert.Single((await host.Api.GetWorkspaceAsync(visible)).Buckets);
        Assert.Empty((await host.Api.GetWorkspaceAsync(new(false, false, "adults_only"))).Buckets);

        Assert.Equal("Assigned", (await host.Assign(entry.WorkId, 222)).Outcome);
        var workspace = await host.Api.GetWorkspaceAsync(new(false, false, "adults_only"));
        Assert.Equal(entry.OwnershipId, Assert.Single(workspace.Buckets).OwnershipId);
        var corrected = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal((entry.WorkId, "Corrected game", "Corrected summary", 2020),
            (corrected.WorkId, corrected.Title, corrected.Summary, corrected.FirstReleaseYear));
        Assert.Null(corrected.BackgroundUrl);
        var after = await host.Details.GetAsync(entry.WorkId);
        Assert.Empty(after.Images);
        Assert.Empty(after.Ratings);
        Assert.Empty(await host.Services.GetRequiredService<IWorkMaturityRepository>().GetForWorkAsync(entry.WorkId));
        using var check = host.Services.GetRequiredService<ISqliteConnectionFactory>().Open();
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
    }

    private sealed record Entry(long WorkId, long ReleaseId, long OwnershipId);

    private sealed class Host(string directory, WebApplication app, FixtureIgdb provider, WinnowApiClient api) : IAsyncDisposable
    {
        public IServiceProvider Services => app.Services;
        public WinnowApiClient Api => api;
        public FixtureIgdb Provider => provider;
        public DetailsClient Details { get; } = new(api);
        public WinnowApiClient Reopen() => WinnowApiClient.Attach(directory);
        public static async Task<Host> Start(bool hasCover = true, bool service = true, bool configured = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-igdb-assignment-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var provider = new FixtureIgdb(hasCover, configured);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
            {
                services.RemoveAll<IIgdbClient>();
                services.AddSingleton<IIgdbClient>(provider);
                if (!service) services.RemoveAll<IIgdbAssignmentService>();
            });
            await app.StartAsync();
            return new(directory, app, provider, WinnowApiClient.Attach(directory));
        }
        public async Task<Entry> Seed(string title = "Prey", long? igdbId = 1234, string? cover = WrongCover, int year = 2006, string store = "gog")
        {
            var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new()
                { Name = title, IgdbId = igdbId, FirstReleaseYear = year, Publisher = "2K Games", Summary = "A Cherokee garage mechanic is abducted.", CoverUrl = cover });
            var release = await Services.GetRequiredService<IReleaseRepository>().InsertAsync(new()
                { WorkId = work, Name = title, Platform = "windows" });
            if (store == "steam")
                await Services.GetRequiredService<IReleaseRepository>().AddExternalIdAsync(new()
                    { ReleaseId = release, Provider = "steam", ProviderId = "3900" });
            var ownership = await Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new()
                { ReleaseId = release, Store = store });
            await Services.GetRequiredService<IPlayRecordRepository>().InsertAsync(new()
                { OwnershipId = ownership, PlaytimeMinutes = 120, LastPlayedAt = LastPlayed, Source = store, ObservedAt = Observed });
            return new(work, release, ownership);
        }
        public async Task<MutationOutcome> Assign(long workId, long id)
            => await Details.AssignIgdbAsync(workId, new(id, (await Details.GetIgdbStateAsync(workId)).Revision));
        public async Task<CoverKey?> Cover(long workId)
            => (await Api.GetAsync<ArtworkState>($"works/{workId}/artwork/Cover")).Current?.PreviewKey;
        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixtureIgdb(bool hasCover, bool configured) : IIgdbClient
    {
        public List<string> Searches { get; } = [];
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(configured);
        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbGame>>(ids.Select(id => id switch
            {
                5678 => new IgdbGame(5678, "Prey", hasCover ? RightCover : null, 2017, "Morgan Yu wakes on Talos I.", [], [], ["Bethesda Softworks"])
                    { Platforms = ["PC (Microsoft Windows)", "PlayStation 4"] },
                222 => new IgdbGame(222, "Corrected game", null, 2020, "Corrected summary", [], [], []),
                _ => throw new InvalidOperationException($"Unexpected IGDB fixture lookup: {id}"),
            }).ToArray());
        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            Searches.Add(title);
            return Task.FromResult<IReadOnlyList<IgdbSearchResult>>([new(5678, "Prey", hasCover ? RightCover : null, 2017, ["PC (Microsoft Windows)", "PlayStation 4"])]);
        }
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(new Dictionary<string, IgdbExternalMatch>());
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int source, IEnumerable<string> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(new Dictionary<string, IgdbExternalMatch>());
        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<long, IgdbAgeRatings>>(new Dictionary<long, IgdbAgeRatings>());
    }
}
