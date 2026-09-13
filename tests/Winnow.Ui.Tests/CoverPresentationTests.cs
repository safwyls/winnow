using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CoverPresentationTests
{
    [AvaloniaFact]
    public void Inherited_fit_changes_existing_and_new_portrait_images_but_leaves_backgrounds_alone()
    {
        var root = new Panel();
        var cover = new Image();
        CoverPresentation.SetIsCover(cover, true);
        var background = new Image { Stretch = Stretch.UniformToFill };
        root.Children.Add(cover);
        root.Children.Add(background);
        Assert.Equal(Stretch.Uniform, cover.Stretch);
        CoverPresentation.SetFit(root, false);
        Assert.Equal(Stretch.UniformToFill, cover.Stretch);
        var later = new Image();
        CoverPresentation.SetIsCover(later, true);
        root.Children.Add(later);
        Assert.Equal(Stretch.UniformToFill, later.Stretch);
        CoverPresentation.SetFit(root, true);
        Assert.Equal(Stretch.Uniform, cover.Stretch);
        Assert.Equal(Stretch.Uniform, later.Stretch);
        Assert.Equal(Stretch.UniformToFill, background.Stretch);
    }

    [AvaloniaFact]
    public void Desktop_cover_views_apply_fit_without_changing_their_bounds()
    {
        foreach (var view in new Control[] { new GameTileView(), new FeedCardView(), new RowCoverView() })
        {
            var root = new Border { Child = view, Width = 400, Height = 300 };
            var window = new Window { Content = root, Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var images = view.GetVisualDescendants().OfType<Image>().Where(CoverPresentation.GetIsCover).ToArray();
            Assert.Equal(2, images.Length);
            var bounds = images.Select(image => image.Bounds).ToArray();
            Assert.All(images, image => Assert.Equal(Stretch.Uniform, image.Stretch));
            CoverPresentation.SetFit(root, false);
            root.Measure(new Size(400, 300));
            root.Arrange(new Rect(0, 0, 400, 300));
            Assert.All(images, image => Assert.Equal(Stretch.UniformToFill, image.Stretch));
            Assert.Equal(bounds, images.Select(image => image.Bounds));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Desktop_display_selector_updates_shared_preference_and_root_presentation()
    {
        var shell = PreviewData.Shell;
        var original = shell.Display.FitCoverArt;
        shell.Display.FitCoverArt = true;
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = window.FindControl<Button>("DisplayButton")!;
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var selector = Assert.IsAssignableFrom<Control>(flyout.Content).GetLogicalDescendants()
                .OfType<ComboBox>().Single(control => control.Name == "CoverArtFitSelector");
            Assert.Equal(0, selector.SelectedIndex);
            selector.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.Display.FitCoverArt);
            Assert.False(CoverPresentation.GetFit(window));
            selector.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Display.FitCoverArt);
            Assert.True(CoverPresentation.GetFit(window));
        }
        finally
        {
            window.Close();
            shell.Display.FitCoverArt = original;
        }
    }
}
