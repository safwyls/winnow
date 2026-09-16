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
    public void Every_closed_contour_has_a_full_circuit_and_continuous_trail_at_the_seam()
    {
        // The bundled thirteen paths contain fifteen closed figures: head, inner
        // details and detached pieces. No contour may disappear from the animation.
        Assert.Equal(15, LoadingDragon.TraceContours.Count);
        foreach (var contour in LoadingDragon.TraceContours)
        {
            Assert.True(Assert.Single(Assert.IsType<PathGeometry>(contour).Figures!).IsClosed);
            var length = contour.ContourLength;
            Assert.True(length > 0);
            for (var sample = 0; sample <= 100; sample++)
            {
                var phase = sample / 100d;
                var segments = LoadingDragon.TraceSegments(contour, phase).ToArray();
                Assert.NotEmpty(segments);
                // Skia flattens the extracted curves again when measuring them;
                // one source unit is less than a fifth of a pixel at either UI size.
                Assert.InRange(segments.Sum(segment => segment.ContourLength), length * .13 - 1, length * .13 + 1);
                var leading = segments[0];
                Assert.True(leading.TryGetPointAtDistance(leading.ContourLength, out var actual));
                Assert.True(contour.TryGetPointAtDistance(phase == 0 ? length : phase * length, out var expected));
                var delta = actual - expected;
                Assert.InRange(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), 0, .1);
            }
        }
    }

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
            Assert.False(dragon.HasCompletedCircuit);
            Frame(1800);
            Assert.True(dragon.HasCompletedCircuit);
            Frame(2250);
            Assert.Equal(.25, dragon.Phase);
            Assert.True(dragon.HasCompletedCircuit);
            Assert.Single(callbacks);
            dragon.IsTracing = false;
            Frame(900);
            Assert.Equal(0, dragon.Phase);
            Assert.False(dragon.HasCompletedCircuit);
            Assert.Empty(callbacks);
            Assert.False(dragon.HasCompletedCircuit);
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

    [AvaloniaFact]
    public async Task Compositor_draws_continuous_circuits_and_keeps_progress_across_theme_changes()
    {
        var dragon = new LoadingDragon { Width = 100, Height = 100, IsTracing = true };
        var window = new Window { Width = 320, Height = 260, Content = dragon,
            Background = new SolidColorBrush(Color.Parse("#0F1C1E")) };
        var ink = new SolidColorBrush(Colors.AntiqueWhite);
        var glow = new SolidColorBrush(Colors.Turquoise);
        window.Resources["Text"] = ink;
        window.Resources["Volt"] = glow;
        try
        {
            window.Show();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!dragon.HasCompletedCircuit && DateTime.UtcNow < deadline)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                await Task.Delay(16);
            }
            Assert.True(dragon.HasCompletedCircuit);
            Assert.True(dragon.RenderedFrameCount > 2);
            var before = dragon.RenderedFrameCount;
            ink.Color = Colors.White;
            glow.Color = Colors.Teal;
            dragon.Width = 140;
            for (var i = 0; i < 3; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                await Task.Delay(16);
            }
            Assert.True(dragon.HasCompletedCircuit);
            Assert.True(dragon.RenderedFrameCount > before);
            using var image = window.CaptureRenderedFrame();
            Assert.NotNull(image);
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                image.Save(Path.Combine(directory, "dragon-compositor.png"));
            }
            dragon.IsTracing = false;
            Assert.False(dragon.HasCompletedCircuit);
            dragon.IsTracing = true;
            Assert.False(dragon.HasCompletedCircuit);
            window.Content = null;
            Assert.False(dragon.HasCompletedCircuit);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 88)]
    [InlineData(true, 88)]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    public void Trace_renders_theme_colors_at_different_positions(bool light, int size)
    {
        var callbacks = new List<Action<TimeSpan>>();
        var dragon = new LoadingDragon { Width = size, Height = size, IsTracing = true,
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
            for (var i = 0; i <= 8; i++)
            {
                var frameCallbacks = callbacks.ToArray(); callbacks.Clear();
                foreach (var callback in frameCallbacks) callback(TimeSpan.FromMilliseconds(i * 225));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var image = window.CaptureRenderedFrame();
                Assert.NotNull(image);
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
                {
                    Directory.CreateDirectory(directory);
                    image.Save(Path.Combine(directory, $"dragon-trace-{size}-{light}-{i}.png"));
                }
            }
        }
        finally { window.Close(); }
    }
}
