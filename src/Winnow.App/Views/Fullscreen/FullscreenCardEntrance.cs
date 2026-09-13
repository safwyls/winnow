using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Winnow.App.Views.Fullscreen;

internal static class FullscreenCardEntrance
{
    public static void Attach(FullscreenContext context, FullscreenCover cover, Control title)
    {
        // Fade the art inside the cover so the selected border stays visible throughout.
        var art = cover.Child!;
        var elapsed = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var top = new GradientStop(Colors.Transparent, 0);
        var bottom = new GradientStop(Colors.Transparent, 1);
        var mask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
            GradientStops = [top, bottom]
        };

        void Finish()
        {
            timer.Stop();
            elapsed.Reset();
            art.OpacityMask = null;
            art.Opacity = title.Opacity = 1;
        }

        void PreferencesChanged(object? sender, EventArgs e)
        {
            if (context.ReducedMotion) Finish();
        }

        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(elapsed.Elapsed.TotalMilliseconds / 240, 0, 1);
            if (context.ReducedMotion || progress >= 1) { Finish(); return; }
            var eased = 1 - Math.Pow(1 - progress, 3);
            var lowerOpacity = Math.Clamp(eased * 2 - 1, 0, 1);
            top.Color = Color.FromArgb((byte)(255 * Math.Min(eased * 2, 1)), 255, 255, 255);
            bottom.Color = Color.FromArgb((byte)(255 * lowerOpacity), 255, 255, 255);
            art.Opacity = 1;
            title.Opacity = lowerOpacity;
        };
        cover.AttachedToVisualTree += (_, _) =>
        {
            context.PreferencesChanged += PreferencesChanged;
            if (context.ReducedMotion) { Finish(); return; }
            top.Color = bottom.Color = Colors.Transparent;
            art.OpacityMask = mask;
            art.Opacity = title.Opacity = 0;
            elapsed.Restart();
            timer.Start();
        };
        cover.DetachedFromVisualTree += (_, _) =>
        {
            context.PreferencesChanged -= PreferencesChanged;
            Finish();
        };
    }
}
