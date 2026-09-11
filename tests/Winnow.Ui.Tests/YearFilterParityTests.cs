using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class YearFilterParityTests
{
    public static TheoryData<bool, string, string, bool> Ranges => new()
    {
        { false, "999", "2020", false }, { true, "999", "2020", false },
        { false, "1000", "9999", true }, { true, "1000", "9999", true },
        { false, "9999", "9999", true }, { true, "9999", "9999", true },
        { false, "nope", "2020", false }, { true, "nope", "2020", false },
        { false, "", "", true }, { true, "", "", true },
        { false, "2030", "2020", false }, { true, "2030", "2020", false },
        { false, " 1000 ", " 9999 ", true }, { true, " 1000 ", " 9999 ", true },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Ranges))]
    public async Task Both_surfaces_validate_the_same_range_and_invalid_edits_preserve_the_applied_filter(
        bool fullscreen, string from, string to, bool valid)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 3);
        using (var connection = db.Factory.Open()) connection.Execute("UPDATE works SET first_release_year = CASE id WHEN 1 THEN 2015 WHEN 2 THEN 2025 END;");
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory));
        await library.LoadCommand.ExecuteAsync(null);
        library.Filters.Apply(new LibraryFilter { YearFrom = 2010, YearTo = 2020 });
        Assert.Single(library.VisibleTiles);
        using var feed = new FeedViewModel(new PreviewFeedService(), library);
        using var context = new FullscreenContext(library, feed, PreviewData.Shell);
        using var page = new FullscreenBrowseFiltersPage(context);
        var backs = 0;
        context.BackRequested += () => backs++;
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen
            ? page : new FilterPanelView { DataContext = library.Filters } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (fullscreen)
            {
                string draft = from;
                context.TextRequested += field => field.Text = draft;
                ActivateYear(page, "Release year from");
                draft = to;
                ActivateYear(page, "Release year to");
                page.Handle(GamepadButtons.Keyboard);
            }
            else
            {
                var fields = window.GetVisualDescendants().OfType<TextBox>().ToArray();
                Assert.Single(fields, field => AutomationProperties.GetName(field) == "From this year").Text = from;
                Assert.Single(fields, field => AutomationProperties.GetName(field) == "Up to this year").Text = to;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(fullscreen && valid ? 1 : 0, backs);
            if (valid)
            {
                Assert.True(ReleaseYearRange.TryParse(from, to, out var expected));
                Assert.Equal(expected.From, library.Filters.YearFrom);
                Assert.Equal(expected.To, library.Filters.YearTo);
                Assert.False(library.Filters.HasYearProblem);
            }
            else
            {
                Assert.Equal(2010, library.Filters.YearFrom);
                Assert.Equal(2020, library.Filters.YearTo);
                Assert.Single(library.VisibleTiles);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block =>
                    block.IsEffectivelyVisible && block.Text == ReleaseYearRange.ValidationMessage);
            }
        }
        finally { window.Close(); }
    }

    private static void ActivateYear(FullscreenPage page, string prefix)
    {
        var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
            AutomationProperties.GetName(button)?.StartsWith(prefix, StringComparison.Ordinal) == true);
        Assert.True(button.Focus());
        page.Handle(GamepadButtons.Accept);
        Dispatcher.UIThread.RunJobs();
    }
}
