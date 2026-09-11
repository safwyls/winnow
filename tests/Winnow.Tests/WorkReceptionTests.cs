using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam.Model;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Round-trip tests for <c>work_images</c> and <c>work_ratings</c> (migration
/// 0028) through <see cref="WorkReceptionWriter"/>. Verifies the per-source
/// isolation, the absence-as-no-row rule, the change-counting contract, and
/// CASCADE deletion.
/// </summary>
public sealed class WorkReceptionTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly WorkImageRepository _images;
    private readonly WorkRatingRepository _ratings;
    private readonly WorkReceptionWriter _writer;

    public WorkReceptionTests()
    {
        _works = new WorkRepository(_db.Factory);
        _images = new WorkImageRepository(_db.Factory);
        _ratings = new WorkRatingRepository(_db.Factory);
        _writer = new WorkReceptionWriter(_images, _ratings, new IgdbObservationWriter(_db.Factory));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Screenshots_and_artworks_round_trip_in_the_order_igdb_gave_them()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(screenshots: ["sc1", "sc2", "sc3"], artworks: ["ar1"]));

        var rows = await _images.GetForWorkAsync(workId);

        var screenshots = Assert.Single(rows, r => r.Kind == ImageKinds.Screenshot);
        Assert.Equal(ImageSources.Igdb, screenshots.Source);
        Assert.Equal(["sc1", "sc2", "sc3"], screenshots.Ids);

        var artworks = Assert.Single(rows, r => r.Kind == ImageKinds.Artwork);
        Assert.Equal(["ar1"], artworks.Ids);
    }

    [Fact]
    public async Task A_game_with_no_screenshots_gets_no_row_rather_than_an_empty_one()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game());

        Assert.Empty(await _images.GetForWorkAsync(workId));
    }

    [Fact]
    public async Task Screenshots_igdb_has_withdrawn_are_removed_rather_than_left_stale()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(screenshots: ["sc1", "sc2"]));
        Assert.NotEmpty(await _images.GetForWorkAsync(workId));

        var changed = await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game());

        Assert.Equal(1, changed);
        Assert.Empty(await _images.GetForWorkAsync(workId));
    }

    [Fact]
    public async Task The_two_igdb_figures_are_stored_apart_each_with_its_count()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(
            new IgdbMappingVersion(workId, 1, 0), Game(userRating: 78.5, userCount: 1204, criticRating: 84, criticCount: 37));

        var rows = await _ratings.GetForWorkAsync(workId);

        var users = Assert.Single(rows, r => r.Source == RatingSources.IgdbUsers);
        Assert.Equal(78.5, users.Score);
        Assert.Equal(1204, users.RatingCount);
        Assert.Null(users.Label);

        var critics = Assert.Single(rows, r => r.Source == RatingSources.IgdbCritics);
        Assert.Equal(84d, critics.Score);
        Assert.Equal(37, critics.RatingCount);
    }

    [Fact]
    public async Task Steam_stores_its_own_label_beside_the_percentage_and_the_count()
    {
        var workId = await SeedAsync();

        await _writer.ApplySteamAsync(
            workId,
            new SteamStoreReviewSummary(41_203, 91) { ReviewScore = 8, Label = "Very Positive" });

        var steam = Assert.Single(await _ratings.GetForWorkAsync(workId));

        Assert.Equal(RatingSources.Steam, steam.Source);
        Assert.Equal(91d, steam.Score);
        Assert.Equal(41_203, steam.RatingCount);
        Assert.Equal("Very Positive", steam.Label);
    }

    [Fact]
    public async Task A_figure_with_no_ratings_behind_it_is_not_stored()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(userRating: 90, userCount: 0));
        await _writer.ApplySteamAsync(workId, SteamStoreReviewSummary.None);

        Assert.Empty(await _ratings.GetForWorkAsync(workId));
    }

    [Fact]
    public async Task One_source_never_clobbers_another()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(userRating: 78.5, userCount: 1204));
        await _writer.ApplySteamAsync(
            workId, new SteamStoreReviewSummary(41_203, 91) { Label = "Very Positive" });

        var rows = await _ratings.GetForWorkAsync(workId);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Source == RatingSources.IgdbUsers);
        Assert.Contains(rows, r => r.Source == RatingSources.Steam);
    }

    [Fact]
    public async Task Writing_the_same_answer_twice_changes_nothing()
    {
        var workId = await SeedAsync();
        var game = Game(screenshots: ["sc1"], userRating: 78.5, userCount: 1204);

        Assert.Equal(2, await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), game));
        Assert.Equal(0, await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), game));
    }

    [Fact]
    public async Task A_moved_figure_is_written_again()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(userRating: 78.5, userCount: 1204));

        Assert.Equal(1, await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(userRating: 78.5, userCount: 1205)));
    }

    [Fact]
    public async Task Deleting_a_work_takes_its_reception_rows_with_it()
    {
        var workId = await SeedAsync();

        await _writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 1, 0), Game(screenshots: ["sc1"], userRating: 78.5, userCount: 9));

        using (var lease = _db.Factory.Lease())
        {
            using var command = lease.Connection.CreateCommand();
            command.Transaction = lease.Transaction;
            command.CommandText = "DELETE FROM works WHERE id = " + workId;
            command.ExecuteNonQuery();
        }

        Assert.Empty(await _images.GetForWorkAsync(workId));
        Assert.Empty(await _ratings.GetForWorkAsync(workId));
    }

    private static IgdbGame Game(
        IReadOnlyList<string>? screenshots = null,
        IReadOnlyList<string>? artworks = null,
        double? userRating = null,
        int? userCount = null,
        double? criticRating = null,
        int? criticCount = null)
        => new(1, "A Game", null, null, null, [], [], [])
        {
            ScreenshotImageIds = screenshots ?? [],
            ArtworkImageIds = artworks ?? [],
            UserRating = userRating,
            UserRatingCount = userCount,
            CriticRating = criticRating,
            CriticRatingCount = criticCount,
        };

    private async Task<long> SeedAsync()
        => await _works.InsertAsync(new Work { Name = "A Game", IgdbId = 1 });
}
