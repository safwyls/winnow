using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class MergeDormancyTests
{
    [AvaloniaFact]
    public async Task Visible_merge_covers_follow_the_display_toggle_without_reloading()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownership = new OwnershipRepository(db.Factory);
        var candidates = new MergeCandidateRepository(db.Factory);
        var links = new IdentityLinkRepository(db.Factory);
        var refusals = new ExpansionRefusalRepository(db.Factory);
        async Task<long> Seed(string store)
        {
            var work = await works.InsertAsync(new Work { Name = "Bastion", FirstReleaseYear = 2011, Publisher = "Supergiant Games" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Bastion", Platform = "windows" });
            await ownership.UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
            return release;
        }
        var steam = await Seed("steam");
        var epic = await Seed("epic");
        MatchSubject Subject(long id) => new() { ReleaseId = id, Title = "Bastion", ReleaseYear = 2011, Publisher = "Supergiant Games" };
        var score = new SoftMatcher().Score(Subject(steam), Subject(epic));
        Assert.True(score.ShouldQueue);
        await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = steam, RightReleaseId = epic, Score = score.Score,
            SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending,
        });
        var ramp = new DormancyRamp();
        var display = new DisplaySettingsViewModel(ramp);
        var queue = new MergeQueueViewModel(candidates, releases, works, links, ownership,
            new LibraryExpansionScan(releases, links, refusals), refusals,
            new LibraryQueryRepository(db.Factory), ramp: ramp);
        await queue.LoadCommand.ExecuteAsync(null);
        var view = new MergeQueueView { DataContext = queue };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var vividImages = view.GetVisualDescendants().OfType<Image>()
                .Where(image => image.DataContext is MergeRowViewModel && image.Parent is Panel panel &&
                    panel.Children.OfType<Image>().Last() == image).ToArray();
            Assert.Equal(2, vividImages.Length);
            Assert.All(vividImages, image => Assert.Equal(0, image.Opacity));
            display.DimDormantCovers = false;
            Dispatcher.UIThread.RunJobs();
            Assert.All(vividImages, image => Assert.Equal(1, image.Opacity));
            display.DimDormantCovers = true;
            Dispatcher.UIThread.RunJobs();
            Assert.All(vividImages, image => Assert.Equal(0, image.Opacity));
        }
        finally { window.Close(); }
    }
}
