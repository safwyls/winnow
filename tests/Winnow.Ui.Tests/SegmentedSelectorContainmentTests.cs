using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Themes;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SegmentedSelectorContainmentTests
{
    [AvaloniaTheory]
    [InlineData("winnow")]
    [InlineData("rose-pine-dawn")]
    public async Task Segment_states_preserve_the_rounded_group_boundary(string themeId)
    {
        var theme = WinnowThemes.ById(themeId);
        var buttons = new[] { Segment("First"), Segment("Middle"), Segment("Last") };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in buttons) row.Children.Add(button);
        var clip = new Border { Classes = { "segment-clip" }, Child = row };
        // Fourfold geometry gives fully covered pixels outside the inner arc,
        // avoiding assertions against renderer-specific antialiasing fractions.
        var outer = new Border
        {
            BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(theme.Line), Background = new SolidColorBrush(theme.Ground),
            Width = 392, Height = 120, Child = clip,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20)
        };
        var window = new Window
        {
            Width = 440, Height = 160, Content = outer, Background = new SolidColorBrush(theme.Surface),
            RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark
        };
        foreach (var (key, color) in theme.Tokens(0)) window.Resources[key] = new SolidColorBrush(color);
        window.Resources["RadiusControlInner"] = new CornerRadius(12);
        window.Show();
        try
        {
            window.MouseMove(new Point(1, 1));
            window.FocusManager!.ClearFocus();
            await Pump(window);
            Assert.Equal(new CornerRadius(12), clip.CornerRadius);
            using var baseline = Frame(window);
            Capture(baseline, $"segments-{themeId}-baseline");
            var pixels = Pixels(baseline);
            var originalBounds = buttons.Select(button => button.Bounds).ToArray();
            var outerBounds = outer.Bounds;
            foreach (var index in new[] { 0, 2 })
            {
                var button = buttons[index];
                button.Classes.Add("on");
                await Check("selected");
                if (index == 0)
                {
                    clip.ClipToBounds = false;
                    await Pump(window);
                    using var unclipped = Frame(window);
                    var corner = clip.TranslatePoint(new Point(1, 1), window)!.Value;
                    var offset = ((int)corner.Y * unclipped.PixelSize.Width + (int)corner.X) * 4;
                    Assert.False(pixels.AsSpan(offset, 4).SequenceEqual(Pixels(unclipped).AsSpan(offset, 4)),
                        "The pixel check must detect the original square fill when clipping is disabled.");
                    clip.ClearValue(Visual.ClipToBoundsProperty);
                }
                button.Classes.Remove("on");
                window.MouseMove(Center(button, window));
                await Check("hover");
                window.MouseDown(Center(button, window), MouseButton.Left);
                await Check("pressed");
                window.MouseMove(new Point(1, 1));
                window.MouseUp(new Point(1, 1), MouseButton.Left);
                window.FocusManager.ClearFocus();
                Assert.True(button.Focus(NavigationMethod.Tab));
                await Check("focus");
                window.FocusManager.ClearFocus();

                async Task Check(string state)
                {
                    await Pump(window);
                    Assert.Equal(outerBounds, outer.Bounds);
                    Assert.Equal(originalBounds, buttons.Select(item => item.Bounds).ToArray());
                    if (state == "focus")
                    {
                        Assert.True(button.IsKeyboardFocusWithin);
                        var ring = button.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "SegmentFocusBorder");
                        Assert.Equal(new Thickness(2), ring.Margin);
                        Assert.NotEqual(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
                    }
                    using var frame = Frame(window);
                    Capture(frame, $"segments-{themeId}-{index}-{state}");
                    var actual = Pixels(frame);
                    if (state == "focus")
                    {
                        Assert.False(pixels.SequenceEqual(actual), "Keyboard focus must remain visible above the clipped fill.");
                        return;
                    }
                    if (state == "selected")
                    {
                        var inside = button.TranslatePoint(new Point(20, 20), window)!.Value;
                        var insideOffset = ((int)inside.Y * frame.PixelSize.Width + (int)inside.X) * 4;
                        Assert.False(pixels.AsSpan(insideOffset, 4).SequenceEqual(actual.AsSpan(insideOffset, 4)),
                            "The selected segment must still paint its interior.");
                    }
                    // At every end, these pixels sit outside the rounded inner
                    // clip but inside the square a segment would otherwise paint.
                    var origin = clip.TranslatePoint(default, window)!.Value;
                    foreach (var right in new[] { false, true })
                    foreach (var bottom in new[] { false, true })
                    for (var dx = 0; dx < 2; dx++)
                    for (var dy = 0; dy < 2; dy++)
                    {
                        var x = (int)origin.X + (right ? (int)clip.Bounds.Width - 1 - dx : dx);
                        var y = (int)origin.Y + (bottom ? (int)clip.Bounds.Height - 1 - dy : dy);
                        var offset = (y * frame.PixelSize.Width + x) * 4;
                        Assert.True(pixels.AsSpan(offset, 4).SequenceEqual(actual.AsSpan(offset, 4)),
                            $"{themeId}/{index}/{state}: segment paint crossed the inner curve at {x},{y}: {Convert.ToHexString(pixels.AsSpan(offset, 4))} -> {Convert.ToHexString(actual.AsSpan(offset, 4))}.");
                    }
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("shell", 2)]
    [InlineData("platforms", 1)]
    [InlineData("merges", 1)]
    public void Production_segment_groups_use_an_inner_clip(string surface, int expectedGroups)
    {
        Control content = surface switch
        {
            "shell" => new MainWindow { DataContext = PreviewData.Shell },
            "platforms" => new StoresView { DataContext = PreviewData.Stores },
            _ => new MergeQueueView { DataContext = PreviewData.MergeQueue }
        };
        var groups = content.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("segment-clip")).ToArray();
        Assert.Equal(expectedGroups, groups.Length);
        foreach (var group in groups)
        {
            Assert.IsType<Border>(group.Parent);
            Assert.NotNull(group.Child);
        }
    }

    private static Button Segment(string text) => new()
    {
        Classes = { "seg", "tab" }, Width = 128, Height = 112,
        Content = new TextBlock { Text = text }
    };

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static async Task Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        await Task.Delay(180);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Bitmap Frame(Window window)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        return frame;
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    private static void Capture(Bitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
