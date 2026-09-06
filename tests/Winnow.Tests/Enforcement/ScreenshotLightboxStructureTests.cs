using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Structural rules the screenshot lightbox must keep, read off the markup. The
/// load-bearing rule is that it is an overlay in the window's own visual tree
/// and not a popup: §10.7 forbids flyouts in the detail modal because a popup
/// is its own root with no adorner layer, so <c>FocusAdorner</c> draws nothing
/// there. The modal is not a popup and neither is this, which is why the rule
/// does not bite.
/// </summary>
public sealed class ScreenshotLightboxStructureTests
{
    private const string View = "src/Winnow.App/Views/ScreenshotLightboxView.axaml";

    private const string Window = "src/Winnow.App/Views/MainWindow.axaml";

    /// <summary>No <c>Popup</c>, <c>Flyout</c>, or <c>ContextFlyout</c>
    /// anywhere in the overlay. A popup is its own root with no adorner layer,
    /// so the focus rings would stop drawing.</summary>
    [Fact]
    public void The_overlay_is_not_a_popup()
    {
        var markup = RepositoryTree.Read(View);

        Assert.DoesNotContain("<Popup", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<Flyout", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<MenuFlyout", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextFlyout", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("FlyoutBase", markup, StringComparison.Ordinal);
    }

    /// <summary>The overlay is declared in the window's own <c>Grid</c> after
    /// the detail modal and spanning the same three columns, so it is in the
    /// window's visual tree and draws over the modal.</summary>
    [Fact]
    public void It_is_declared_in_the_windows_grid_after_the_modal()
    {
        var markup = RepositoryTree.Read(Window);

        var modal = markup.IndexOf("<views:GameDetailsView", StringComparison.Ordinal);
        var overlay = markup.IndexOf("<views:ScreenshotLightboxView", StringComparison.Ordinal);

        Assert.True(modal >= 0, "The detail modal is no longer hosted in the window's grid.");
        Assert.True(overlay >= 0, "The screenshot lightbox is no longer hosted in the window's grid.");
        Assert.True(overlay > modal, "The lightbox is declared before the modal, so it draws underneath it.");

        var tag = Regex.Match(
            markup,
            @"<views:ScreenshotLightboxView(.*?)/>",
            RegexOptions.Singleline);

        Assert.True(tag.Success);
        Assert.Contains("Grid.ColumnSpan=\"3\"", tag.Value, StringComparison.Ordinal);

        // Bound off Library, never off Library.Details: a binding whose path
        // passes through a null resolves to UnsetValue, and IsVisible would
        // fall back to its own default of true.
        Assert.Contains("Library.Lightbox.IsOpen", tag.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Details.", tag.Value, StringComparison.Ordinal);
    }

    /// <summary>Tab cannot reach the modal underneath while the overlay is up.
    /// The trap is <c>TabNavigation="Cycle"</c>, the same one the modal's own
    /// card carries.</summary>
    [Fact]
    public void Focus_is_trapped_inside_the_overlay()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Contains(
            "KeyboardNavigation.TabNavigation=\"Cycle\"",
            markup,
            StringComparison.Ordinal);
    }

    /// <summary>1282 x 722 is 1280 x 720 plus the 1px border on each side;
    /// 1280 x 720 is the native size of IGDB's <c>t_screenshot_huge</c>.
    /// <c>Stretch</c> is <c>Uniform</c>, so a small window shrinks the whole
    /// frame rather than cropping it. <c>UniformToFill</c> must not come
    /// back.</summary>
    [Fact]
    public void The_frame_is_capped_at_the_shots_native_size_and_never_cropped()
    {
        var markup = RepositoryTree.Read(View);

        var frame = Regex.Match(
            markup,
            @"<Border Name=""Frame""(.*?)</Border>",
            RegexOptions.Singleline);

        Assert.True(frame.Success, "The lightbox frame is gone.");
        Assert.Contains("MaxWidth=\"1282\"", frame.Value, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"722\"", frame.Value, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", frame.Value, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", frame.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("UniformToFill", frame.Value, StringComparison.Ordinal);
    }

    /// <summary>The surface names itself as a dialog and states which shot is
    /// showing. Avalonia 11.3.20 has no <c>IsDialog</c>, so
    /// <c>ControlTypeOverride="Window"</c> is the closest type and the words
    /// are in the name. The position also rides a <c>TextBlock</c> marked
    /// <c>Polite</c>, because changing a <c>Name</c> at runtime raises no UIA
    /// event and navigating would otherwise be silent.</summary>
    [Fact]
    public void It_announces_itself_as_a_dialog_and_says_which_shot_is_up()
    {
        var markup = RepositoryTree.Read(View);

        var overlay = Regex.Match(markup, @"<Panel Name=""Overlay""(.*?)>", RegexOptions.Singleline);

        Assert.True(overlay.Success, "The overlay panel is gone.");
        Assert.Contains(
            "AutomationProperties.ControlTypeOverride=\"Window\"",
            overlay.Value,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AccessibilityView=\"Control\"",
            overlay.Value,
            StringComparison.Ordinal);
        Assert.Contains("{Binding AutomationName}", overlay.Value, StringComparison.Ordinal);

        var caption = Regex.Match(
            markup,
            @"<TextBlock Grid\.Row=""1""(.*?)/>",
            RegexOptions.Singleline);

        Assert.True(caption.Success, "The position caption is gone.");
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", caption.Value, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Caption}\"", caption.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name", caption.Value, StringComparison.Ordinal);
    }

    /// <summary>Three controls — close, previous shot, next shot — each a real
    /// <c>Button</c> with an accessible name and a tooltip. Escape and the
    /// arrow keys are the keyboard equivalents, answered in
    /// <c>MainWindow.OnKeyDown</c> above the modal's own layer so one press of
    /// Escape leaves the modal standing.</summary>
    [Fact]
    public void It_carries_a_close_control_and_navigation_with_keyboard_equivalents()
    {
        var markup = RepositoryTree.Read(View);

        foreach (var name in new[] { "CloseButton", "PreviousButton", "NextButton" })
        {
            var button = Regex.Match(
                markup,
                @"<Button [^>]*Name=""" + name + @"""(.*?)>",
                RegexOptions.Singleline);

            Assert.True(button.Success, $"{name} is gone from the lightbox.");
            Assert.Contains("AutomationProperties.Name=", button.Value, StringComparison.Ordinal);
            Assert.Contains("ToolTip.Tip=", button.Value, StringComparison.Ordinal);
        }

        var keys = RepositoryTree.Read("src/Winnow.App/Views/MainWindow.axaml.cs");

        var layer = Regex.Match(
            keys,
            @"if \(_library\?\.Lightbox is \{ IsOpen: true \} lightbox\)(.*?)\n        \}",
            RegexOptions.Singleline);

        Assert.True(layer.Success, "The window no longer answers keys for the lightbox.");
        Assert.Contains("case Key.Escape:", layer.Value, StringComparison.Ordinal);
        Assert.Contains("case Key.Left:", layer.Value, StringComparison.Ordinal);
        Assert.Contains("case Key.Right:", layer.Value, StringComparison.Ordinal);

        // Above the modal's own layer, so Escape unwinds one layer per press.
        var modalLayer = keys.IndexOf("if (_library is { IsDetailsOpen: true })", StringComparison.Ordinal);
        Assert.True(modalLayer > 0);
        Assert.True(layer.Index < modalLayer, "The lightbox answers keys after the modal, so Escape closes both.");
    }
}
