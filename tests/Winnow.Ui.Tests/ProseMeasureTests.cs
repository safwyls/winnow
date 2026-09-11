using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ProseMeasureTests
{
    [AvaloniaTheory]
    [InlineData(700)]
    [InlineData(1400)]
    public void Desktop_prose_keeps_its_measure_and_left_edge_across_reading_surfaces(double width)
    {
        var stores = PreviewData.Stores;
        var window = new Window { Width = width, Height = 900 };
        try
        {
            window.Show();
            foreach (var view in new UserControl[]
            {
                new StoresView { DataContext = stores },
                new AccountStatsView { DataContext = PreviewData.AccountStats },
                new ApplicationSettingsView { DataContext = PreviewData.ApplicationSettings },
                new LibrarySettingsView { DataContext = PreviewData.LibrarySettings },
                new AppearanceView { DataContext = PreviewData.Appearance },
                new MergeQueueView { DataContext = PreviewData.MergeQueue },
            })
            {
                window.Content = view;
                Dispatcher.UIThread.RunJobs();
                var prose = view.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && (text.Classes.Contains("para") || text.Classes.Contains("prose"))).ToArray();
                Assert.NotEmpty(prose);
                Assert.All(prose, text =>
                {
                    Assert.Equal(410, text.MaxWidth);
                    Assert.Equal(HorizontalAlignment.Left, text.HorizontalAlignment);
                    Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                    Assert.InRange(text.Bounds.Width, 0, 410);
                });
            }
            window.Content = new StoresView { DataContext = stores };
            stores.OpenSignInConsentCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var consent = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == stores.SteamSignInCostsMessage);
            Assert.True(consent.Bounds.Height > 18);
            Assert.InRange(consent.Bounds.Width, 1, 410);
        }
        finally { stores.CloseModalCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void Fullscreen_prose_retains_the_TV_typography_and_available_width()
    {
        var text = FullscreenUi.Text(string.Join(" ", Enumerable.Repeat("Readable television paragraphs use the available layout width.", 8)));
        var window = new Window { Width = 1200, Height = 900, Content = text };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(28, text.FontSize);
            Assert.True(double.IsPositiveInfinity(text.MaxWidth));
            Assert.True(text.Bounds.Width > 410);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        }
        finally { window.Close(); }
    }
}
