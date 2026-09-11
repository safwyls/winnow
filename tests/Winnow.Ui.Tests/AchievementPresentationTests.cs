using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class AchievementPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false, 0, "Not fetched")]
    [InlineData(true, 0, "Not fetched")]
    [InlineData(false, 1, "No achievements")]
    [InlineData(true, 1, "No achievements")]
    [InlineData(false, 2, "0/1 · 0%")]
    [InlineData(true, 2, "0/1 · 0%")]
    [InlineData(false, 3, "Unavailable")]
    [InlineData(true, 3, "Unavailable")]
    [InlineData(false, 4, "1/1 · 100% · last known")]
    [InlineData(true, 4, "1/1 · 100% · last known")]
    public async Task Both_surfaces_render_ingested_account_states_without_inventing_zero(bool fullscreen, int state, string expected)
    {
        using var db = new TempDatabase();
        using (var connection = db.Factory.Open()) connection.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'Achievement fixture','Achievement fixture');
            INSERT INTO releases(id,work_id,name) VALUES(1,1,'Achievement fixture');
            """);
        var now = DateTime.UtcNow;
        var repository = new AchievementRepository(db.Factory);
        var known = new AchievementFetch { AttemptedAt = now, Schema = [new("A", "First", null, false)],
            Unlocks = new Dictionary<string, DateTime?>() };
        if (state == 1) await repository.SaveAsync(1, "12345", known with { Schema = [], Unlocks = null });
        if (state == 2) await repository.SaveAsync(1, "12345", known);
        if (state == 3) await repository.SaveAsync(1, "12345", new AchievementFetch { AttemptedAt = now });
        if (state == 4)
        {
            await repository.SaveAsync(1, "12345", known with { Unlocks = new Dictionary<string, DateTime?> { ["A"] = null } });
            await repository.SaveAsync(1, "12345", new AchievementFetch { AttemptedAt = now.AddSeconds(1) });
        }
        var summary = Assert.Single(await repository.GetForAccountAsync([1], "12345", now.AddSeconds(1)));
        var entry = new CoverageEntry { OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = "Achievement fixture", Store = "steam", PlaytimeMinutes = 0 };
        var coverage = new GameCoverageViewModel(IdentityCoverage.For(1, SameGameResolution.Empty, [entry]),
            new Dictionary<long, string> { [1] = entry.Title }, new Dictionary<long, ReleaseAchievementSummary> { [1] = summary });
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: entry.Title), "Never played", [], now, coverage: coverage)
            { SelectedTabIndex = 4 };
        var control = fullscreen ? (Control)new FullscreenDetailsPage(null!, details, 3)
            : new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1920, Height = 1080, Content = control };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible
                && text.Text == (fullscreen ? "Achievements: " + expected : expected));
        }
        finally { window.Close(); }
    }
}
