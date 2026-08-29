using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="WorkRepository.ApplyEnrichmentAsync"/> is documented as fill-only:
/// an established fact must survive any later non-null response from a weaker
/// or changed source. F03 found the SQL doing the opposite
/// (<c>COALESCE(@Incoming, stored_value)</c>, which lets the incoming value win)
/// for six columns. Null-input tests alone cannot catch that regression — a
/// wrong-direction COALESCE still fills a null column correctly. Each fact
/// below therefore gets two tests: one proving a stored value resists a
/// conflicting incoming value, one proving a null stored value still gets
/// filled.
/// </summary>
public class WorkRepositoryEnrichmentTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;

    public WorkRepositoryEnrichmentTests() => _works = new WorkRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    private async Task<long> SeedAsync(Work work) => await _works.InsertAsync(work);

    [Fact]
    public async Task Established_first_release_year_resists_a_conflicting_incoming_year()
    {
        var workId = await SeedAsync(new Work { Name = "Deep Rock Galactic", FirstReleaseYear = 2018 });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, FirstReleaseYear: 2020));

        var work = await _works.GetAsync(workId);
        Assert.Equal(2018, work!.FirstReleaseYear);
    }

    [Fact]
    public async Task Null_first_release_year_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Deep Rock Galactic" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, FirstReleaseYear: 2018));

        var work = await _works.GetAsync(workId);
        Assert.Equal(2018, work!.FirstReleaseYear);
    }

    [Fact]
    public async Task Established_summary_resists_a_conflicting_incoming_summary()
    {
        var workId = await SeedAsync(new Work { Name = "Prey", Summary = "Established summary." });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Summary: "A different, later summary."));

        var work = await _works.GetAsync(workId);
        Assert.Equal("Established summary.", work!.Summary);
    }

    [Fact]
    public async Task Null_summary_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Prey" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Summary: "Filled summary."));

        var work = await _works.GetAsync(workId);
        Assert.Equal("Filled summary.", work!.Summary);
    }

    [Fact]
    public async Task Established_cover_url_resists_a_conflicting_incoming_cover_url()
    {
        var workId = await SeedAsync(new Work { Name = "Prey", CoverUrl = "https://example/established.jpg" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, CoverUrl: "https://example/different.jpg"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("https://example/established.jpg", work!.CoverUrl);
    }

    [Fact]
    public async Task Null_cover_url_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Prey" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, CoverUrl: "https://example/filled.jpg"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("https://example/filled.jpg", work!.CoverUrl);
    }

    [Fact]
    public async Task Established_publisher_resists_a_conflicting_incoming_publisher()
    {
        var workId = await SeedAsync(new Work { Name = "Prey", Publisher = "Bethesda Softworks" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Publisher: "A Different Publisher"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("Bethesda Softworks", work!.Publisher);
    }

    [Fact]
    public async Task Null_publisher_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Prey" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Publisher: "Bethesda Softworks"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("Bethesda Softworks", work!.Publisher);
    }

    [Fact]
    public async Task Established_steam_app_type_resists_a_conflicting_incoming_type()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2", SteamAppType = "game" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, SteamAppType: "demo"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("game", work!.SteamAppType);
    }

    [Fact]
    public async Task Null_steam_app_type_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, SteamAppType: "game"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("game", work!.SteamAppType);
    }

    [Fact]
    public async Task Established_epic_categories_resists_a_conflicting_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Fez", EpicCategories = "games" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, EpicCategories: "games/edition/base"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("games", work!.EpicCategories);
    }

    [Fact]
    public async Task Null_epic_categories_is_filled_by_incoming_value()
    {
        var workId = await SeedAsync(new Work { Name = "Fez" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, EpicCategories: "games"));

        var work = await _works.GetAsync(workId);
        Assert.Equal("games", work!.EpicCategories);
    }

    /// <summary>
    /// One call, every column conflicting at once — guards against a fix that
    /// only reverses the operands for the first column checked and leaves the
    /// rest untouched.
    /// </summary>
    [Fact]
    public async Task A_single_conflicting_enrichment_leaves_every_established_column_untouched()
    {
        var workId = await SeedAsync(new Work
        {
            Name = "Prey",
            FirstReleaseYear = 2006,
            Summary = "Established summary.",
            CoverUrl = "https://example/established.jpg",
            Publisher = "2K Games",
            SteamAppType = "game",
            EpicCategories = "games",
        });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(
            workId,
            FirstReleaseYear: 2017,
            Summary: "A different, later summary.",
            CoverUrl: "https://example/different.jpg",
            Publisher: "Bethesda Softworks",
            SteamAppType: "demo",
            EpicCategories: "games/edition/base"));

        var work = await _works.GetAsync(workId);
        Assert.NotNull(work);
        Assert.Equal(2006, work.FirstReleaseYear);
        Assert.Equal("Established summary.", work.Summary);
        Assert.Equal("https://example/established.jpg", work.CoverUrl);
        Assert.Equal("2K Games", work.Publisher);
        Assert.Equal("game", work.SteamAppType);
        Assert.Equal("games", work.EpicCategories);
    }
}
