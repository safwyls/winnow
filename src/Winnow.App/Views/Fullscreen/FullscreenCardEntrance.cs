using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Winnow.App.Views.Fullscreen;

internal static class FullscreenCardEntrance
{
    public static void Attach(FullscreenContext context, FullscreenCover cover, Control title, int column)
    {
        // Fade the art inside the cover so the selected border stays visible throughout.
        var art = cover.Child!;
        var elapsed = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var delay = Math.Min(Math.Max(column, 0) * 24, 120);

        void Finish()
        {
            timer.Stop();
            elapsed.Reset();
            art.Opacity = title.Opacity = 1;
        }

        void PreferencesChanged(object? sender, EventArgs e)
        {
            if (context.ReducedMotion) Finish();
        }

        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp((elapsed.Elapsed.TotalMilliseconds - delay) / 180, 0, 1);
            if (context.ReducedMotion || progress >= 1) { Finish(); return; }
            art.Opacity = title.Opacity = 1 - Math.Pow(1 - progress, 3);
        };
        cover.AttachedToVisualTree += (_, _) =>
        {
            context.PreferencesChanged += PreferencesChanged;
            if (context.ReducedMotion) { Finish(); return; }
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
