using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CollectionExplanationTests
{
    [AvaloniaFact]
    public void Built_in_collections_expose_explanations_on_desktop_and_fullscreen()
    {
        var shell = PreviewData.Shell;
        var buckets = shell.Library.Buckets.Prepend(shell.Library.AllGames).ToArray();
        Assert.Equal("Invested", buckets.Single(b => b.Key == LibraryBuckets.Retired).Name);
        var window = new MainWindow { Width = 1200, Height = 800, DataContext = shell };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var bucket in buckets)
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.Classes.Contains("bucket") && ReferenceEquals(b.CommandParameter, bucket));
                Assert.False(string.IsNullOrWhiteSpace(bucket.Description));
                Assert.Equal(bucket.Description, ToolTip.GetTip(button));
                Assert.Equal(bucket.Description, AutomationProperties.GetHelpText(button));
                ToolTip.SetIsOpen(button, true); Dispatcher.UIThread.RunJobs();
                Assert.True(ToolTip.GetIsOpen(button));
                ToolTip.SetIsOpen(button, false);
            }
        }
        finally { window.Close(); }

        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, shell);
        using var filters = new FullscreenBrowseFiltersPage(context);
        FullscreenPage? choices = null;
        context.PageRequested += page => choices = page;
        var fullscreen = new Window { Width = 1920, Height = 1080, Content = filters };
        try
        {
            fullscreen.Show(); Dispatcher.UIThread.RunJobs();
            filters.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b)?.StartsWith("Collection ·") == true)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(choices);
            fullscreen.Content = choices; Dispatcher.UIThread.RunJobs();
            foreach (var bucket in buckets)
            {
                var button = choices.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetName(b) == bucket.Name);
                Assert.Equal(bucket.Description, ToolTip.GetTip(button));
                Assert.Equal(bucket.Description, AutomationProperties.GetHelpText(button));
                Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == bucket.Description);
            }
        }
        finally { fullscreen.Close(); choices?.Dispose(); }
    }
}
