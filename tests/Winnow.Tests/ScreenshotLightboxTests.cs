using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The screenshot lightbox: opening on the pressed shot, wrapping in both
/// directions, closing, and the single-screenshot case that draws no navigation
/// at all. Nothing here needs a rendering platform — the view model holds the
/// position, the strip's selection mark, and the accessible strings; the view
/// holds only where focus goes.
/// </summary>
public sealed class ScreenshotLightboxTests
{
    private static WorkImages Images(string ids)
        => new()
        {
            WorkId = 1,
            Source = ImageSources.Igdb,
            Kind = ImageKinds.Screenshot,
            ImageIds = ids,
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    private static (GameScreenshotsViewModel Strip, ScreenshotLightboxViewModel Box) Build(string ids)
    {
        var box = new ScreenshotLightboxViewModel();
        var strip = GameScreenshotsViewModel.From([Images(ids)], covers: null, box);
        Assert.NotNull(strip);
        return (strip!, box);
    }

    /// <summary>Nothing is up until a thumbnail is pressed. The overlay opens
    /// on the shot that was pressed, not on the first one.</summary>
    [Fact]
    public void It_opens_on_the_shot_that_was_pressed()
    {
        var (strip, box) = Build("aa1,bb2,cc3");

        Assert.False(box.IsOpen);
        Assert.Empty(box.Caption);

        strip.SelectCommand.Execute(strip.Shots[2]);

        Assert.True(box.IsOpen);
        Assert.Equal(ScreenshotLightboxCopy.Caption(3, 3), box.Caption);
        Assert.Equal(ScreenshotLightboxCopy.DialogAutomationName(3, 3), box.AutomationName);
        Assert.True(strip.Shots[2].IsSelected);
    }

    /// <summary>Navigation wraps in both directions rather than dead-ending.
    /// The strip's selection mark follows, so the thumbnails still show which
    /// shot is up.</summary>
    [Fact]
    public void Navigation_wraps_in_both_directions_and_the_strip_follows()
    {
        var (strip, box) = Build("aa1,bb2,cc3");

        strip.SelectCommand.Execute(strip.Shots[0]);

        box.PreviousCommand.Execute(null);
        Assert.Equal(ScreenshotLightboxCopy.Caption(3, 3), box.Caption);
        Assert.True(strip.Shots[2].IsSelected);

        box.NextCommand.Execute(null);
        Assert.Equal(ScreenshotLightboxCopy.Caption(1, 3), box.Caption);
        Assert.True(strip.Shots[0].IsSelected);

        box.NextCommand.Execute(null);
        box.NextCommand.Execute(null);
        Assert.Equal(ScreenshotLightboxCopy.Caption(3, 3), box.Caption);
        Assert.Single(strip.Shots, s => s.IsSelected);
    }

    /// <summary>A game with one screenshot draws no navigation controls rather
    /// than two inert ones, and the arrow keys do nothing.</summary>
    [Fact]
    public void One_screenshot_draws_no_navigation_at_all()
    {
        var (strip, box) = Build("aa1");

        strip.SelectCommand.Execute(strip.Shots[0]);

        Assert.True(box.IsOpen);
        Assert.False(box.CanNavigate);

        box.NextCommand.Execute(null);
        box.PreviousCommand.Execute(null);

        Assert.Equal(ScreenshotLightboxCopy.Caption(1, 1), box.Caption);
    }

    /// <summary>Closing drops the bitmap, the largest object the view model
    /// holds. The strip keeps its selection mark so the user can see where they
    /// got to. A second close is quiet.</summary>
    [Fact]
    public void Closing_drops_the_image_and_leaves_the_strips_mark_standing()
    {
        var (strip, box) = Build("aa1,bb2");

        strip.SelectCommand.Execute(strip.Shots[1]);
        box.CloseCommand.Execute(null);

        Assert.False(box.IsOpen);
        Assert.Null(box.Image);
        Assert.True(strip.Shots[1].IsSelected);

        box.CloseCommand.Execute(null);
        Assert.False(box.IsOpen);
    }

    /// <summary>Navigating a closed overlay does nothing, so the window's
    /// arrow-key layer cannot walk a strip that is not on screen.</summary>
    [Fact]
    public void A_closed_overlay_does_not_navigate()
    {
        var (strip, box) = Build("aa1,bb2,cc3");

        strip.SelectCommand.Execute(strip.Shots[0]);
        box.CloseCommand.Execute(null);
        box.NextCommand.Execute(null);

        Assert.False(box.IsOpen);
        Assert.Equal(ScreenshotLightboxCopy.Caption(1, 3), box.Caption);
    }

    /// <summary>The accessible name names the surface as a dialog and states
    /// the position and the count. Avalonia 11.3.20 has no <c>IsDialog</c> and
    /// <c>PositionInSet</c> / <c>SizeOfSet</c> are read by nothing (§8), so
    /// both facts must be in the string.</summary>
    [Fact]
    public void The_name_spells_out_both_the_role_and_the_count()
    {
        var name = ScreenshotLightboxCopy.DialogAutomationName(2, 7);

        Assert.Contains("2", name, StringComparison.Ordinal);
        Assert.Contains("7", name, StringComparison.Ordinal);
        Assert.Contains("dialog", name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Both <c>TODO(docs-writer)</c> and <c>PLACEHOLDER_*</c> markers have
    /// reached a user of this project. Every string this overlay speaks or shows
    /// is guarded against both, so a stub that survives a commit is caught
    /// before it ships. The same guard <c>GameActionBandCopy</c> carries.
    /// </summary>
    [Fact]
    public void No_string_the_overlay_speaks_is_ever_a_placeholder()
    {
        string[] strings =
        [
            ScreenshotLightboxCopy.DialogAutomationName(2, 5),
            ScreenshotLightboxCopy.Caption(2, 5),
            ScreenshotLightboxCopy.CloseAutomationName,
            ScreenshotLightboxCopy.CloseTooltip,
            ScreenshotLightboxCopy.PreviousAutomationName,
            ScreenshotLightboxCopy.PreviousTooltip,
            ScreenshotLightboxCopy.NextAutomationName,
            ScreenshotLightboxCopy.NextTooltip,
            GameScreenshotsCopy.ThumbnailTooltip(2, 5),
        ];

        foreach (var text in strings)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("TODO", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PLACEHOLDER", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The drawn cap is the native size of IGDB's
    /// <c>t_screenshot_huge</c>, the rendition the strip's <c>CoverKey</c>
    /// resolves to. Past it every pixel is upscale, so the overlay never grows
    /// beyond it.</summary>
    [Fact]
    public void The_cap_is_the_shots_own_native_size()
    {
        Assert.Equal(1280, ScreenshotLightboxViewModel.ImageWidth);
        Assert.Equal(720, ScreenshotLightboxViewModel.ImageHeight);
    }
}
