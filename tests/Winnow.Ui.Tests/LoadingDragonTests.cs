using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LoadingDragonTests
{
    [AvaloniaFact]
    public void Trace_moves_with_frame_time_and_stops_on_disable_and_detach()
    {
        var callbacks = new List<Action<TimeSpan>>();
        var dragon = new LoadingDragon { Width = 140, Height = 140, IsTracing = true,
            FrameScheduler = callback => callbacks.Add(callback) };
        var window = new Window { Width = 320, Height = 260, Content = dragon };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Frame(0); Frame(450);
            Assert.Equal(.25, dragon.Phase);
            Assert.Single(callbacks);
            dragon.IsTracing = false;
            Frame(900);
            Assert.Equal(0, dragon.Phase);
            Assert.Empty(callbacks);
            dragon.IsTracing = true;
            Frame(1000); Frame(1450);
            Assert.Equal(.25, dragon.Phase);
            window.Content = null;
            Frame(1700);
            Assert.Empty(callbacks);
        }
        finally { window.Close(); }

        void Frame(int milliseconds)
        {
            var frame = callbacks.ToArray(); callbacks.Clear();
            foreach (var callback in frame) callback(TimeSpan.FromMilliseconds(milliseconds));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Trace_renders_theme_colors_at_different_positions(bool light)
    {
        var callbacks = new List<Action<TimeSpan>>();
        var dragon = new LoadingDragon { Width = 160, Height = 160, IsTracing = true,
            FrameScheduler = callback => callbacks.Add(callback) };
        var window = new Window { Width = 320, Height = 260, Content = dragon,
            Background = light ? Brushes.WhiteSmoke : new SolidColorBrush(Color.Parse("#0F1C1E")) };
        window.Resources["Text"] = light ? Brushes.DarkSlateGray : Brushes.AntiqueWhite;
        window.Resources["Volt"] = light ? Brushes.Teal : Brushes.Turquoise;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Same(window.Resources["Text"], dragon.Ink);
            Assert.Same(window.Resources["Volt"], dragon.Glow);
            for (var i = 0; i < 4; i++)
            {
                var frameCallbacks = callbacks.ToArray(); callbacks.Clear();
                foreach (var callback in frameCallbacks) callback(TimeSpan.FromMilliseconds(i * 450));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var image = window.CaptureRenderedFrame();
                Assert.NotNull(image);
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
                {
                    Directory.CreateDirectory(directory);
                    image.Save(Path.Combine(directory, $"dragon-trace-{light}-{i}.png"));
                }
            }
        }
        finally { window.Close(); }
    }
}
