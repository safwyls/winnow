using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenRowViewportTests
{
    [AvaloniaFact]
    public void Moving_down_retains_rows_and_translates_without_fading()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        var original = viewport.GetRow(1);
        viewport.Show(1);
        fixture.Window.UpdateLayout();
        Assert.True(viewport.IsAnimating);
        Assert.Same(original, viewport.GetRow(1));
        var start = Top(original);
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(110));
        Assert.InRange(Top(original), 0, start - 1);
        Assert.All(viewport.RealizedRows.Values, row => { Assert.Equal(1, row.Opacity); Assert.Null(row.OpacityMask); });
        Assert.False(viewport.GetRow(0).IsEnabled);
        Assert.False(viewport.GetRow(0).IsHitTestVisible);
        var target = Assert.IsType<Button>(Assert.IsType<Border>(original).Child);
        Assert.True(target.Focus());
        Assert.Same(target, fixture.Window.FocusManager!.GetFocusedElement());
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
        Assert.False(viewport.IsAnimating);
        Assert.Equal(0, Top(original), 5);
    }

    [AvaloniaFact]
    public void Reversal_starts_at_current_position_and_moves_up()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        viewport.Show(1);
        fixture.Window.UpdateLayout();
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(90));
        var original = viewport.GetRow(0);
        var before = Top(original);
        viewport.Show(0);
        fixture.Window.UpdateLayout();
        Assert.Equal(before, Top(original), 5);
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(110));
        Assert.InRange(Top(original), before + 1, 0);
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
        Assert.Equal(0, Top(original), 5);
        Assert.True(original.IsEnabled);
    }

    [AvaloniaFact]
    public void Long_jumps_and_rapid_navigation_keep_realization_bounded()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        viewport.Show(900);
        fixture.Window.UpdateLayout();
        Assert.False(viewport.IsAnimating);
        Assert.Equal(new[] { 899, 900, 901, 902 }, viewport.RealizedRows.Keys.Order().ToArray());
        for (var row = 901; row < 970; row++)
        {
            viewport.Show(row);
            fixture.Window.UpdateLayout();
            Assert.InRange(viewport.RealizedRows.Count, 1, 2 * 2 + 3);
        }
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
        Assert.Equal(4, viewport.RealizedRows.Count);
        viewport.Show(int.MaxValue);
        Assert.Equal(998, viewport.FirstRow);
        viewport.Show(-50);
        Assert.Equal(0, viewport.FirstRow);
    }

    [AvaloniaFact]
    public void Reduced_motion_and_resize_snap_mid_flight()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        viewport.Show(1);
        fixture.Window.UpdateLayout();
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(80));
        fixture.Context.ReducedMotion = true;
        Assert.False(viewport.IsAnimating);
        Assert.Equal(0, Top(viewport.GetRow(1)), 5);
        viewport.Show(2);
        fixture.Window.UpdateLayout();
        Assert.False(viewport.IsAnimating);
        Assert.Equal(0, Top(viewport.GetRow(2)), 5);
        fixture.Context.ReducedMotion = false;
        viewport.Show(3);
        fixture.Window.UpdateLayout();
        Assert.True(viewport.IsAnimating);
        viewport.Height = 500;
        fixture.Window.UpdateLayout();
        Assert.False(viewport.IsAnimating);
        Assert.Equal(0, Top(viewport.GetRow(3)), 5);
    }

    [AvaloniaFact]
    public void Detach_releases_rows_and_reattach_restores_target_without_motion()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        viewport.Show(1);
        var old = viewport.RealizedRows.Values.ToArray();
        var detached = 0;
        foreach (var row in old) row.DetachedFromVisualTree += (_, _) => detached++;
        fixture.Window.Content = null;
        Assert.False(viewport.IsAnimating);
        Assert.Empty(viewport.RealizedRows);
        Assert.Empty(viewport.Children);
        Assert.Equal(old.Length, detached);
        fixture.Window.Content = viewport;
        fixture.Window.UpdateLayout();
        Assert.False(viewport.IsAnimating);
        Assert.Equal(1, viewport.FirstRow);
        Assert.Equal(0, Top(viewport.GetRow(1)), 5);
        Assert.DoesNotContain(viewport.GetRow(1), old);
        viewport.Dispose();
        fixture.Context.ReducedMotion = true;
        Assert.Empty(viewport.RealizedRows);
    }

    [AvaloniaFact]
    public void Row_refresh_keeps_neighbors_and_notifies_only_for_realization_changes()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        var neighbor = viewport.GetRow(1);
        var old = viewport.GetRow(0);
        var changed = 0;
        viewport.RowsChanged += (_, _) => changed++;
        viewport.InvalidateRow(0);
        Assert.NotSame(old, viewport.GetRow(0));
        Assert.Same(neighbor, viewport.GetRow(1));
        Assert.Equal(1, changed);
        viewport.Show(0);
        viewport.InvalidateRow(500);
        Assert.Equal(1, changed);
        viewport.Show(1);
        var afterShow = changed;
        viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(110));
        Assert.Equal(afterShow, changed);
    }

    [AvaloniaFact]
    public void Data_replacement_clears_outgoing_rows_and_handles_empty_data()
    {
        using var fixture = new Fixture();
        var viewport = fixture.Viewport;
        viewport.Show(1);
        viewport.Configure(1, 2, _ => new Border());
        fixture.Window.UpdateLayout();
        Assert.False(viewport.IsAnimating);
        Assert.Equal(0, viewport.FirstRow);
        Assert.Single(viewport.RealizedRows);
        viewport.Configure(0, 2, _ => throw new InvalidOperationException());
        viewport.Show(20);
        Assert.Empty(viewport.RealizedRows);
        Assert.Equal(0, viewport.FirstRow);
    }

    private static double Top(Control control) => control.Bounds.Y + Assert.IsType<TranslateTransform>(control.RenderTransform).Y;

    private sealed class Fixture : IDisposable
    {
        public FullscreenContext Context { get; }
        public FullscreenRowViewport Viewport { get; }
        public Window Window { get; }

        public Fixture()
        {
            Context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
            Viewport = new FullscreenRowViewport(Context);
            Viewport.Configure(1000, 2, index => new Border { Child = new Button { Content = $"Row {index}" } });
            Window = new Window { Width = 800, Height = 600, Content = Viewport };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.Close();
            Viewport.Dispose();
            Context.ReducedMotion = false;
            Context.Dispose();
        }
    }
}
