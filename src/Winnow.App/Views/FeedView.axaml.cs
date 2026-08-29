using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Feed. Implements keyboard navigation: Left/Right walk
/// the sequence across sections, Up/Down move by row respecting the live
/// column count.
/// </summary>
public partial class FeedView : UserControl
{
    public FeedView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Answers an arrow key over the Feed. Returns true when it moved focus, so
    /// the window can mark the key handled and nothing downstream — the
    /// library's own selection walk, in particular — also acts on it.
    /// </summary>
    public bool HandleNavigationKey(KeyEventArgs e)
    {
        var (alongRow, acrossRows) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };

        if (alongRow == 0 && acrossRows == 0)
        {
            return false;
        }

        var sections = Sections();
        if (sections.Count == 0)
        {
            return false;
        }

        var (section, index) = Locate(sections);

        // Nothing focused yet: the first arrow key lands on the first card
        // rather than doing nothing, which is what a user pressing it is asking
        // for. Same rule the wall follows on its first key press.
        if (section < 0)
        {
            return Take(sections[0].Cards[0]);
        }

        return acrossRows != 0
            ? MoveByRow(sections, section, index, acrossRows)
            : MoveBySequence(sections, section, index, alongRow);
    }

    /// <summary>
    /// One step along the reading order. Inside a section that is simply the
    /// next card, wrapping rows; at either end of one it is the neighbouring
    /// section's nearest card, so the sequence runs unbroken down the screen.
    /// </summary>
    private static bool MoveBySequence(List<Section> sections, int section, int index, int step)
    {
        var next = index + step;
        if (next >= 0 && next < sections[section].Cards.Count)
        {
            return Take(sections[section].Cards[next]);
        }

        var target = section + step;
        if (target < 0 || target >= sections.Count)
        {
            return false;
        }

        var cards = sections[target].Cards;
        return Take(step > 0 ? cards[0] : cards[^1]);
    }

    /// <summary>
    /// One row up or down. Within a section a row is <see cref="FeedGrid.Columns"/>
    /// cards wide; past its first or last row the move crosses into the section
    /// above or below, entering by its last or first row and keeping the column
    /// the user was in — clamped, because the target row can be shorter and a
    /// move that refuses is worse than a move that lands next door.
    /// </summary>
    private static bool MoveByRow(List<Section> sections, int section, int index, int step)
    {
        var columns = Math.Max(1, sections[section].Columns);
        var next = index + (step * columns);
        if (next >= 0 && next < sections[section].Cards.Count)
        {
            return Take(sections[section].Cards[next]);
        }

        var target = section + step;
        if (target < 0 || target >= sections.Count)
        {
            return false;
        }

        var column = index % columns;
        var cards = sections[target].Cards;

        if (step > 0)
        {
            return Take(cards[Math.Min(column, cards.Count - 1)]);
        }

        // Entering from below: the last row's first card, plus the column —
        // integer division finds that row's start without needing to know how
        // ragged it is.
        var lastRowStart = (cards.Count - 1) / Math.Max(1, sections[target].Columns)
            * Math.Max(1, sections[target].Columns);
        return Take(cards[Math.Min(lastRowStart + column, cards.Count - 1)]);
    }

#if DEBUG
    /// <summary>
    /// <c>--feed-probe</c> plus <c>F9</c>, the wall's probe pointed at this
    /// screen and for the same reason: a wrapping grid's column count and its
    /// arranged rects are precisely what a screenshot cannot tell you, and "one
    /// card per row too many" is invisible until you can read them. Writes to
    /// <c>%TEMP%\winnow-feed-debug.txt</c>.
    ///
    /// <para>Its own flag rather than the wall's, because <c>--grid-probe</c>
    /// forces the library up on startup — the wall cannot be probed from a
    /// screen it is not on, and neither can this.</para>
    /// </summary>
    public void DumpDiagnostics()
    {
        var text = new System.Text.StringBuilder();

        foreach (var grid in this.GetVisualDescendants().OfType<FeedGrid>())
        {
            text.AppendLine(
                $"grid bounds={grid.Bounds} columns={grid.Columns} cards={grid.Children.Count} " +
                $"rows={(grid.Children.Count + grid.Columns - 1) / Math.Max(1, grid.Columns)}");

            foreach (var child in grid.Children)
            {
                text.AppendLine($"  {child.Bounds}");
            }
        }

        System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winnow-feed-debug.txt"),
            $"=== {DateTime.Now:HH:mm:ss.fff} view={Bounds} ===\n{text}\n");
    }
#endif

    /// <summary>One section's grid: its live column count and its cards in presentation order.</summary>
    private readonly record struct Section(int Columns, List<FeedCardView> Cards);

    /// <summary>The sections, in presentation order, skipping any that drew no cards.</summary>
    private List<Section> Sections()
        => this.GetVisualDescendants()
            .OfType<FeedGrid>()
            .Select(grid => new Section(
                grid.Columns,
                grid.Children
                    .Select(child => child as FeedCardView
                        ?? child.GetVisualDescendants().OfType<FeedCardView>().FirstOrDefault())
                    .OfType<FeedCardView>()
                    .ToList()))
            .Where(section => section.Cards.Count > 0)
            .ToList();

    /// <summary>Where focus currently is, as (section, index), or (-1, -1) when it is not on a card.</summary>
    private (int Section, int Index) Locate(List<Section> sections)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is not Visual visual)
        {
            return (-1, -1);
        }

        // The focused element is usually the card's own Button, or the Play
        // button inside it — so walk up to the card that owns it rather than
        // testing for identity.
        var card = visual as FeedCardView
            ?? visual.GetVisualAncestors().OfType<FeedCardView>().FirstOrDefault();

        if (card is null)
        {
            return (-1, -1);
        }

        for (var section = 0; section < sections.Count; section++)
        {
            var index = sections[section].Cards.IndexOf(card);
            if (index >= 0)
            {
                return (section, index);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Moves focus to a card and brings it on screen. One axis now, where the
    /// rails needed two: the Feed has exactly one scroller and it is vertical.
    /// </summary>
    private static bool Take(FeedCardView card)
    {
        card.TakeFocus();
        card.BringIntoView();
        return true;
    }
}
