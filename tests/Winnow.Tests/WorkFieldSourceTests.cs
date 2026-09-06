using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Integration tests for per-field provenance (migration 0027) on temp-file
/// SQLite databases: the editor's set/reset cycle, the interaction between
/// user ownership and automatic enrichment, and the pin's take-it-all gesture.
/// </summary>
public class WorkFieldSourceTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly WorkFieldSourceRepository _fields;
    private readonly WorkIgdbPinRepository _pins;

    public WorkFieldSourceTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _fields = new WorkFieldSourceRepository(_db.Factory);
        _pins = new WorkIgdbPinRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> SeedAsync(Work work, string appId = "620")
    {
        var workId = await _works.InsertAsync(work);
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = work.Name });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = appId,
        });

        return workId;
    }

    [Fact]
    public async Task A_field_the_user_set_carries_the_user_as_its_source()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        Assert.Equal(
            WorkFieldEditOutcome.Applied,
            await _fields.SetFieldAsync(workId, WorkFields.Summary, "Still alive."));

        var sources = await _fields.GetSourcesAsync(workId);
        Assert.Equal(FieldSources.User, sources[WorkFields.Summary]);

        var work = await _works.GetAsync(workId);
        Assert.Equal("Still alive.", work!.Summary);
    }

    [Fact]
    public async Task A_manual_edit_leaves_every_other_field_tracking_its_own_source()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(
            workId, Summary: "A puzzle game.", Publisher: "Valve", FirstReleaseYear: 2011));

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Still alive.");

        var sources = await _fields.GetSourcesAsync(workId);
        Assert.Equal(FieldSources.User, sources[WorkFields.Summary]);
        Assert.Equal(FieldSources.Igdb, sources[WorkFields.Publisher]);
        Assert.Equal(FieldSources.Igdb, sources[WorkFields.FirstReleaseYear]);

        var work = await _works.GetAsync(workId);
        Assert.Equal("Valve", work!.Publisher);
        Assert.Equal(2011, work.FirstReleaseYear);
    }

    [Fact]
    public async Task An_enrichment_pass_leaves_a_field_the_user_owns()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Mine.");
        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Summary: "Theirs."));

        var work = await _works.GetAsync(workId);
        Assert.Equal("Mine.", work!.Summary);

        var sources = await _fields.GetSourcesAsync(workId);
        Assert.Equal(FieldSources.User, sources[WorkFields.Summary]);
    }

    /// <summary>
    /// A field the user deliberately emptied (set then set to null) must not
    /// make the work a target. The enrichment pass would refill it on the
    /// next run, undoing the user's edit and spending a request to do so.
    /// </summary>
    [Fact]
    public async Task An_emptied_field_the_user_owns_is_not_an_enrichment_target()
    {
        var workId = await SeedAsync(new Work
        {
            Name = "Portal 2",
            IgdbId = 7346,
            FirstReleaseYear = 2011,
            CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg",
            Publisher = "Valve",
            IgdbGameType = "Main Game",
            SteamAppType = "Game",
        });

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Mine.");
        await _fields.SetFieldAsync(workId, WorkFields.Summary, null);

        var work = await _works.GetAsync(workId);
        Assert.Null(work!.Summary);

        Assert.Empty(await _works.GetEnrichmentTargetsAsync());
    }

    [Fact]
    public async Task A_field_handed_back_to_automatic_loses_its_stamp_and_is_refilled()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Mine.");
        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Summary: "Theirs."));
        Assert.Equal("Mine.", (await _works.GetAsync(workId))!.Summary);

        Assert.Equal(
            WorkFieldEditOutcome.Applied,
            await _fields.ResetFieldAsync(workId, WorkFields.Summary));

        var afterReset = await _works.GetAsync(workId);
        Assert.Null(afterReset!.Summary);
        Assert.False((await _fields.GetSourcesAsync(workId)).ContainsKey(WorkFields.Summary));

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(workId, Summary: "Theirs."));

        Assert.Equal("Theirs.", (await _works.GetAsync(workId))!.Summary);
        Assert.Equal(
            FieldSources.Igdb,
            (await _fields.GetSourcesAsync(workId))[WorkFields.Summary]);
    }

    [Fact]
    public async Task A_name_handed_back_to_automatic_becomes_provisional_again()
    {
        var workId = await SeedAsync(new Work { Name = "App 620", NameIsProvisional = true });

        await _fields.SetFieldAsync(workId, WorkFields.Name, "My Own Title");
        Assert.False((await _works.GetAsync(workId))!.NameIsProvisional);

        await _fields.ResetFieldAsync(workId, WorkFields.Name);

        var work = await _works.GetAsync(workId);
        Assert.True(work!.NameIsProvisional);
        Assert.Equal("My Own Title", work.Name);
    }

    [Fact]
    public async Task An_enrichment_pass_stamps_each_field_with_the_source_that_supplied_it()
    {
        var workId = await SeedAsync(new Work { Name = "App 620", NameIsProvisional = true });

        await _works.ApplyEnrichmentAsync(new WorkEnrichment(
            workId, Name: "Portal 2", Summary: "A puzzle game.")
        {
            NameSource = FieldSources.Steam,
        });

        var sources = await _fields.GetSourcesAsync(workId);
        Assert.Equal(FieldSources.Steam, sources[WorkFields.Name]);
        Assert.Equal(FieldSources.Igdb, sources[WorkFields.Summary]);
    }

    /// <summary>
    /// Pinning is the "take it all from this record" gesture. It stamps every
    /// field it rewrote as <c>igdb</c>, including fields the user previously
    /// owned, because the user is saying — in the same act — take it all from
    /// this record.
    /// </summary>
    [Fact]
    public async Task A_pin_rewrites_every_field_and_takes_them_all_back_from_the_user()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Mine.");
        await _fields.SetFieldAsync(workId, WorkFields.Publisher, "Me.");

        Assert.Equal(
            WorkIgdbPinOutcome.Pinned,
            await _pins.PinAsync(new WorkIgdbPinAssignment
            {
                WorkId = workId,
                IgdbId = 7346,
                Name = "Portal 2",
                FirstReleaseYear = 2011,
                Summary = "A puzzle game.",
                CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg",
                Publisher = "Valve",
            }));

        var work = await _works.GetAsync(workId);
        Assert.Equal("A puzzle game.", work!.Summary);
        Assert.Equal("Valve", work.Publisher);

        var sources = await _fields.GetSourcesAsync(workId);
        foreach (var field in new[]
                 {
                     WorkFields.Name,
                     WorkFields.FirstReleaseYear,
                     WorkFields.Summary,
                     WorkFields.CoverUrl,
                     WorkFields.Publisher,
                 })
        {
            Assert.Equal(FieldSources.Igdb, sources[field]);
        }
    }

    [Fact]
    public async Task A_field_edited_on_a_pinned_work_is_the_users_and_the_pin_stays_live()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId,
            IgdbId = 7346,
            Name = "Portal 2",
            Summary = "A puzzle game.",
        });

        await _fields.SetFieldAsync(workId, WorkFields.Summary, "Mine.");

        var sources = await _fields.GetSourcesAsync(workId);
        Assert.Equal(FieldSources.User, sources[WorkFields.Summary]);
        Assert.Equal(FieldSources.Igdb, sources[WorkFields.CoverUrl]);
        Assert.NotNull(await _pins.GetAsync(workId));
    }

    [Fact]
    public async Task Background_art_is_a_field_like_any_other()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        await _fields.SetFieldAsync(
            workId, WorkFields.BackgroundUrl, "winnow://user-art/abc123");

        var work = await _works.GetAsync(workId);
        Assert.Equal("winnow://user-art/abc123", work!.BackgroundUrl);

        var state = await _fields.GetStateAsync(workId);
        var background = Assert.Single(state, s => s.Field == WorkFields.BackgroundUrl);
        Assert.True(background.IsUserOwned);
        Assert.Equal("winnow://user-art/abc123", background.Value);
    }

    /// <summary>
    /// An unrecognised field name — including SQL injection attempts — is
    /// <see cref="WorkFieldEditOutcome.UnknownField"/> before any SQL is
    /// built. <c>ColumnFor</c> maps a whitelisted constant to a literal; a
    /// caller's string is never interpolated into a query.
    /// </summary>
    [Fact]
    public async Task An_unknown_field_is_refused_rather_than_reaching_the_sql()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        Assert.Equal(
            WorkFieldEditOutcome.UnknownField,
            await _fields.SetFieldAsync(workId, "name; DROP TABLE works", "boom"));

        Assert.Equal(
            WorkFieldEditOutcome.UnknownField,
            await _fields.ResetFieldAsync(workId, "igdb_id"));
    }

    [Fact]
    public async Task A_year_that_is_not_a_year_is_refused()
    {
        var workId = await SeedAsync(new Work { Name = "Portal 2" });

        Assert.Equal(
            WorkFieldEditOutcome.InvalidValue,
            await _fields.SetFieldAsync(workId, WorkFields.FirstReleaseYear, "soon"));

        Assert.Equal(
            WorkFieldEditOutcome.Applied,
            await _fields.SetFieldAsync(workId, WorkFields.FirstReleaseYear, "2011"));

        Assert.Equal(2011, (await _works.GetAsync(workId))!.FirstReleaseYear);
    }
}
