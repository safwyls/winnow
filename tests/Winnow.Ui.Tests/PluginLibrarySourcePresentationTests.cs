using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PluginLibrarySourcePresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Grouped_history_retains_its_source_explanation_on_both_surfaces(bool fullscreen)
    {
        var now = DateTime.UtcNow;
        var steam = TileEntry.For(1, 1, 1, "steam", 0, null);
        var xbox = TileEntry.For(2, 2, 1, "plugin:xbox", 0, null) with
        { PluginActions = new("Played history — not proof of ownership.", null, null) };
        var tile = TileFixture.Tile(now, [steam, xbox], 1, LibraryBuckets.NeverPlayed);
        using var details = new GameDetailsViewModel(tile, "Never played", [], now);
        var shell = PreviewData.Shell;
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = fullscreen ? new FullscreenDetailsPage(context, details) : null;
        Control view = page is null ? new GameDetailsView { DataContext = details } : page;
        var window = new Window { Width = fullscreen ? 1920 : 1200, Height = fullscreen ? 1080 : 800, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var source = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                block => AutomationProperties.GetAutomationId(block) == "LibrarySourceSummary");
            Assert.True(source.IsEffectivelyVisible);
            Assert.Equal("Xbox: Played history — not proof of ownership.", source.Text);
        }
        finally { window.Close(); }
    }
}
