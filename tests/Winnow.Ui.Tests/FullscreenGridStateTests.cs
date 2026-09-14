using Winnow.App.ViewModels.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenGridStateTests
{
    [Fact]
    public void Vertical_navigation_retains_the_overlapping_row_and_scrolls_only_at_edges()
    {
        var releases = Releases(40);
        var state = new FullscreenGridState();
        state.Resize(6, releases);
        state.Select(3, 2);

        Assert.True(state.MoveVertical(1, releases));
        Assert.Equal(8, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
        Assert.True(state.MoveVertical(1, releases));
        Assert.Equal(14, state.SelectedIndex);
        Assert.Equal(1, state.FirstRow);
        Assert.Equal(8, state.PositionInView);

        Assert.True(state.MoveVertical(-1, releases));
        Assert.Equal(8, state.SelectedIndex);
        Assert.Equal(1, state.FirstRow);
        Assert.True(state.MoveVertical(-1, releases));
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
        Assert.False(state.MoveVertical(-1, releases));
    }

    [Fact]
    public void Partial_final_row_clamps_selection_and_keeps_two_rows_visible()
    {
        var releases = Releases(27);
        var state = new FullscreenGridState();
        state.Resize(6, releases);
        state.Select(24, 23);

        Assert.True(state.MoveVertical(1, releases));
        Assert.Equal(27, state.SelectedReleaseId);
        Assert.Equal(26, state.SelectedIndex);
        Assert.Equal(3, state.FirstRow);
        Assert.Equal(8, state.PositionInView);
        Assert.False(state.MoveVertical(1, releases));
        Assert.True(state.MoveVertical(-1, releases));
        Assert.Equal(20, state.SelectedIndex);
        Assert.Equal(3, state.FirstRow);
    }

    [Fact]
    public void Page_navigation_moves_two_rows_and_clamps_at_both_ends()
    {
        var releases = Releases(27);
        var state = new FullscreenGridState();
        state.Resize(6, releases);
        state.Select(6, 5);

        Assert.True(state.MovePage(1, releases));
        Assert.Equal(17, state.SelectedIndex);
        Assert.Equal(1, state.FirstRow);
        Assert.True(state.MovePage(1, releases));
        Assert.Equal(26, state.SelectedIndex);
        Assert.Equal(3, state.FirstRow);
        Assert.False(state.MovePage(1, releases));
        Assert.True(state.MovePage(-1, releases));
        Assert.Equal(14, state.SelectedIndex);
        Assert.Equal(2, state.FirstRow);
        Assert.True(state.MovePage(-1, releases));
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
        Assert.False(state.MovePage(-1, releases));
    }

    [Fact]
    public void Reorder_and_filter_follow_the_selected_identity_and_reveal_its_new_row()
    {
        var releases = Releases(40);
        var state = new FullscreenGridState();
        state.Resize(6, releases);
        state.Select(35, 34);

        state.Reconcile(releases.Reverse().ToArray());
        Assert.Equal(35, state.SelectedReleaseId);
        Assert.Equal(5, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);

        state.Reconcile([2, 7, 35, 40]);
        Assert.Equal(35, state.SelectedReleaseId);
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
    }

    [Fact]
    public void Removed_selection_uses_the_nearest_remaining_index()
    {
        var state = new FullscreenGridState();
        state.Resize(6, Releases(40));
        state.Select(35, 34);

        state.Reconcile(Releases(15));
        Assert.Equal(15, state.SelectedReleaseId);
        Assert.Equal(14, state.SelectedIndex);
        Assert.Equal(1, state.FirstRow);

        state.Reconcile([70, 80, 90]);
        Assert.Equal(90, state.SelectedReleaseId);
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
    }

    [Fact]
    public void Resize_preserves_identity_and_retains_first_row_when_selection_stays_visible()
    {
        var releases = Releases(71);
        var state = new FullscreenGridState();
        state.Resize(6, releases);
        state.Select(28, 27);
        Assert.Equal(3, state.FirstRow);

        state.Resize(7, releases);
        Assert.Equal(28, state.SelectedReleaseId);
        Assert.Equal(3, state.FirstRow);
        Assert.Equal(6, state.PositionInView);

        state.Resize(12, releases);
        Assert.Equal(28, state.SelectedReleaseId);
        Assert.Equal(2, state.FirstRow);
        Assert.Equal(3, state.PositionInView);

        state.Resize(3, releases);
        Assert.Equal(28, state.SelectedReleaseId);
        Assert.Equal(8, state.FirstRow);
        Assert.Equal(3, state.PositionInView);
    }

    [Fact]
    public void Empty_collection_clears_selection_and_can_be_populated_again()
    {
        var state = new FullscreenGridState();
        state.Resize(6, Releases(40));
        state.Select(35, 34);
        state.Reconcile([]);

        Assert.Null(state.SelectedReleaseId);
        Assert.Equal(0, state.SelectedIndex);
        Assert.Equal(0, state.FirstRow);
        Assert.Equal(0, state.PositionInView);
        Assert.False(state.MoveVertical(1, []));
        Assert.False(state.MoveVertical(-1, []));
        Assert.False(state.MovePage(1, []));
        Assert.False(state.MovePage(-1, []));

        state.Resize(0, [90]);
        Assert.Equal(1, state.Columns);
        Assert.Equal(90, state.SelectedReleaseId);
        Assert.Equal(0, state.FirstRow);
        Assert.False(state.MoveVertical(1, [90]));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    [InlineData(24, 2)]
    [InlineData(27, 3)]
    public void Page_count_accounts_for_partial_rows(int count, int expected)
    {
        var state = new FullscreenGridState();
        Assert.Equal(expected, state.PageCount(count));
    }

    private static long[] Releases(int count) => Enumerable.Range(1, count).Select(i => (long)i).ToArray();
}
