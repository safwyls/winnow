using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DesktopTileFocusTests
{
    [AvaloniaTheory]
    [InlineData(false, NavigationMethod.Tab)]
    [InlineData(false, NavigationMethod.Directional)]
    [InlineData(true, NavigationMethod.Tab)]
    [InlineData(true, NavigationMethod.Directional)]
    public void Focus_restores_vivid_art_and_pointer_exit_preserves_only_keyboard_focus(
        bool feed, NavigationMethod method)
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, ramp: new DormancyRamp { ReducedMotion = true });
        tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        using var card = new FeedCardViewModel(tile, "A forgotten game");
        Control view = feed ? new FeedCardView { DataContext = card, Width = 220 }
            : new GameTileView { DataContext = tile, Width = 220, Height = 330 };
        var elsewhere = new Button { Content = "Elsewhere" };
        var window = new Window { Width = 800, Height = 650,
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { view, elsewhere } } };
        try
        {
            window.Show();
            window.MouseMove(new Point(790, 640));
            Flush();
            var cover = view as GameTileView ?? ((FeedCardView)view).FindControl<GameTileView>("CoverTile")!;
            var art = cover.FindControl<Border>("VividArt")!;
            var target = feed ? ((FeedCardView)view).FindControl<Button>("Card")!
                : cover.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Details");
            Assert.True(tile.DormancyAlpha < 1);
            Assert.Equal(tile.DormancyAlpha, art.Opacity);
            Assert.True(target.Focus(method));
            Flush();
            Assert.Equal(1, art.Opacity);
            window.MouseMove(cover.TranslatePoint(new Point(50, 50), window)!.Value);
            window.MouseMove(new Point(790, 640));
            Flush();
            Assert.Equal(1, art.Opacity);

            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"desktop-focus-{feed}-{method}.png"));
            }

            Assert.True(elsewhere.Focus(NavigationMethod.Tab));
            Flush();
            Assert.Equal(tile.DormancyAlpha, art.Opacity);

            window.MouseMove(cover.TranslatePoint(new Point(50, 50), window)!.Value);
            Assert.True(target.Focus(NavigationMethod.Pointer));
            Flush();
            Assert.Equal(1, art.Opacity);
            window.MouseMove(new Point(790, 640));
            Flush();
            Assert.Equal(tile.DormancyAlpha, art.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Library_selection_updates_rendered_art_without_moving_keyboard_focus()
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, ramp: new DormancyRamp { ReducedMotion = true });
        var view = new GameTileView { DataContext = tile, Width = 220, Height = 330 };
        var window = new Window { Width = 800, Height = 650, Content = view };
        try
        {
            window.Show();
            window.MouseMove(new Point(790, 640));
            Flush();
            var art = view.FindControl<Border>("VividArt")!;
            Assert.True(tile.DormancyAlpha < 1);
            tile.IsSelected = true;
            Flush();
            Assert.Equal(1, art.Opacity);
            tile.IsSelected = false;
            Flush();
            Assert.Equal(tile.DormancyAlpha, art.Opacity);
        }
        finally { window.Close(); }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
