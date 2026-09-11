using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class IgdbMappingRefreshTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correcting_the_mapping_refreshes_open_details_and_removes_old_visibility_evidence(bool fullscreen)
    {
        using var db = new TempDatabase();
        var entry = await new ManualEntryRepository(db.Factory).CreateAsync(new() { Title = "Old game", IgdbId = 111 });
        var facets = new FacetRepository(db.Factory);
        var images = new WorkImageRepository(db.Factory);
        var ratings = new WorkRatingRepository(db.Factory);
        await facets.SetWorkFacetsAsync(entry.WorkId, [new(FacetKinds.Genre, "Old genre")]);
        await new WorkMaturityRepository(db.Factory).UpsertAsync(new() { WorkId = entry.WorkId, Source = "igdb", Ratings = "esrb:ao", ObservedAt = DateTime.UtcNow });
        await images.UpsertAsync(new() { WorkId = entry.WorkId, Source = "igdb", Kind = ImageKinds.Screenshot, ImageIds = "oldshot", ObservedAt = DateTime.UtcNow });
        await ratings.UpsertAsync(new() { WorkId = entry.WorkId, Source = RatingSources.IgdbUsers, Score = 95, RatingCount = 20, ObservedAt = DateTime.UtcNow });
        using (var seed = db.Factory.Open()) seed.Execute("UPDATE works SET background_url='https://example.test/old.jpg' WHERE id=@WorkId; INSERT INTO work_field_sources(work_id,field,source,set_at) VALUES(@WorkId,'background_url','igdb','2026-09-11');", new { entry.WorkId });
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory),
            facets: facets, workImages: images, workRatings: ratings) { ShowExplicitContent = true };
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.AllTiles));
        var details = library.Details!;
        Assert.True(details.ShowScreenshots);
        Assert.True(details.ShowReception);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var tv = new FullscreenDetailsPage(context, details);
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? tv : new GameDetailsView { DataContext = details } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await new WorkIgdbPinRepository(db.Factory).PinAsync(new() { WorkId = entry.WorkId, IgdbId = 222, Name = "Corrected game", Summary = "Corrected summary", FirstReleaseYear = 2020 });
            library.ShowExplicitContent = false;
            await library.LoadCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Corrected game", Assert.Single(library.VisibleTiles).Title);
            Assert.Same(details, library.Details);
            Assert.Equal("Corrected summary", details.Summary);
            Assert.False(details.ShowScreenshots);
            Assert.False(details.ShowReception);
            Assert.Null((await new WorkRepository(db.Factory).GetAsync(entry.WorkId))!.BackgroundUrl);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Corrected summary");
            using var check = db.Factory.Open();
            Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
        }
        finally { window.Close(); library.CloseDetailsCommand.Execute(null); }
    }
}
