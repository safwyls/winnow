using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Keeps the reduced-motion inventory closed. A new AXAML animation must add
/// its own snapping selector and this audit entry; otherwise a new surface can
/// quietly ignore the OS accessibility preference.
/// </summary>
public sealed class ReducedMotionCoverageTests
{
    private static readonly string[] AnimatedFiles =
    [
        "src/Winnow.App/Views/FeedCardView.axaml",
        "src/Winnow.App/Views/GameTileView.axaml",
        "src/Winnow.App/Views/MainWindow.axaml",
        "src/Winnow.App/Views/MergeQueueView.axaml",
    ];

    [Fact]
    public void Every_AXAML_motion_surface_is_in_the_reduced_motion_inventory()
    {
        var found = RepositoryTree.Files("src/Winnow.App", "*.axaml")
            .Where(file => HasMotion(Blanked(RepositoryTree.Read(file))))
            .ToArray();

        Assert.Equal(AnimatedFiles, found);
    }

    [Theory]
    [InlineData("src/Winnow.App/Views/FeedCardView.axaml", "Button.feedcard.snap")]
    [InlineData("src/Winnow.App/Views/GameTileView.axaml", "Border#Lift.snap")]
    [InlineData("src/Winnow.App/Views/MainWindow.axaml", "Window.reducedmotion")]
    [InlineData("src/Winnow.App/Views/MergeQueueView.axaml", "UserControl.reducedmotion")]
    public void Every_motion_surface_has_a_style_that_removes_motion(string file, string reducedSelector)
    {
        var markup = Blanked(RepositoryTree.Read(file));

        Assert.Contains(reducedSelector, markup, StringComparison.Ordinal);
        Assert.Contains("<Transitions/>", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Transition_values_are_never_local_to_an_element()
    {
        var localTransitions = RepositoryTree.Files("src/Winnow.App", "*.axaml")
            .Where(file => Regex.IsMatch(Blanked(RepositoryTree.Read(file)), @"<[A-Za-z_][\w.-]*\.Transitions\b"))
            .ToArray();

        Assert.Empty(localTransitions);
    }

    private static bool HasMotion(string markup) =>
        Regex.IsMatch(markup, @"<(?:[A-Za-z_][\w.-]*\.)?Transitions\b|<Style\.Animations\b|<Animation\b");

    private static string Blanked(string text) =>
        Regex.Replace(
            text,
            "<!--.*?-->",
            match => new string([.. match.Value.Select(c => c is '\r' or '\n' ? c : ' ')]),
            RegexOptions.Singleline);
}
