using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Enrichment target query must cover all stores, not just Steam.
/// </summary>
public class EnrichmentTargetQueryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;

    public EnrichmentTargetQueryTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> SeedAsync(string provider, string providerId, Work work)
    {
        var workId = await _works.InsertAsync(work);
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = work.Name });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = provider,
            ProviderId = providerId,
        });

        return workId;
    }

    [Fact]
    public async Task Returns_every_store_provider_not_just_steam()
    {
        await SeedAsync(ExternalIdProviders.Steam, "620", new Work { Name = "Portal 2" });
        await SeedAsync(ExternalIdProviders.Gog, "1207658695", new Work { Name = "Beneath a Steel Sky" });
        await SeedAsync(ExternalIdProviders.Epic, "7a70b499", new Work { Name = "Fez" });

        var targets = await _works.GetEnrichmentTargetsAsync();

        Assert.Equal(
            [ExternalIdProviders.Epic, ExternalIdProviders.Gog, ExternalIdProviders.Steam],
            targets.Select(t => t.Provider).OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The row has to say which store it came from, or the caller cannot know
    /// which <c>external_game_source</c> to ask IGDB about — and sending a GOG
    /// product id to Steam's source 1 is how you get a confident wrong answer
    /// rather than no answer.
    /// </summary>
    [Fact]
    public async Task Each_target_carries_its_own_provider_and_id()
    {
        await SeedAsync(ExternalIdProviders.Gog, "1207658695", new Work { Name = "Beneath a Steel Sky" });

        var target = Assert.Single(await _works.GetEnrichmentTargetsAsync());
        Assert.Equal(ExternalIdProviders.Gog, target.Provider);
        Assert.Equal("1207658695", target.ProviderId);
    }

    /// <summary>
    /// <c>igdb</c> is Winnow's own canonical identity, not a storefront. Asking
    /// IGDB to resolve an id IGDB gave us is a wasted request at best.
    /// </summary>
    [Fact]
    public async Task Does_not_return_the_igdb_provider()
    {
        var workId = await _works.InsertAsync(new Work { Name = "Portal 2" });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = "Portal 2" });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Igdb,
            ProviderId = "7346",
        });

        Assert.Empty(await _works.GetEnrichmentTargetsAsync());
    }

    /// <summary>
    /// The set still shrinks to nothing as the backlog drains — the widening
    /// must not turn an idempotent pass into one that revisits the whole library
    /// on every launch.
    /// </summary>
    [Fact]
    public async Task A_fully_enriched_work_is_not_returned_whatever_its_store()
    {
        await SeedAsync(
            ExternalIdProviders.Gog,
            "1207658695",
            new Work
            {
                Name = "Beneath a Steel Sky",
                IgdbId = 612,
                FirstReleaseYear = 1994,
                Summary = "Cyberpunk point-and-click.",
                CoverUrl = "https://img/co1.jpg",
                Publisher = "Revolution Software",
                SteamAppType = "Game",

                // Migration 0022. A work carrying an igdb_id but no game_type
                // was enriched before the relation fields were asked for, and
                // is a target again for exactly one pass. "Fully enriched" now
                // includes knowing what kind of thing IGDB says this is.
                IgdbGameType = "main_game",
            });

        Assert.Empty(await _works.GetEnrichmentTargetsAsync());
    }

    /// <summary>
    /// One work reachable under two stores — the shape a confirmed cross-store
    /// merge leaves behind — yields one row per external id, because each is a
    /// distinct route to the same metadata and the caller may need either.
    /// </summary>
    [Fact]
    public async Task A_work_with_two_store_ids_yields_one_row_per_id()
    {
        var workId = await _works.InsertAsync(new Work { Name = "Fez" });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = "Fez" });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = "224760",
        });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Epic,
            ProviderId = "7a70b499",
        });

        var targets = await _works.GetEnrichmentTargetsAsync();

        Assert.Equal(2, targets.Count);
        Assert.All(targets, t => Assert.Equal(workId, t.WorkId));
    }
}
