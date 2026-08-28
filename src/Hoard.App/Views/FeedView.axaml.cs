using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the Feed. Its one job is §8's keyboard floor: the whole
/// interface reachable without a pointer.
///
/// <para><b>What comes free and what does not.</b> Each card is a
/// <see cref="Button"/>, so Tab and Shift+Tab already walk every card in reading
/// order, Enter and Space already open the detail modal, and tokens.axaml's
/// adorner already draws the 2px Volt ring. What does not come free is the
/// shape: a feed is a grid of rails, and a user who has learned that arrow keys
/// walk the cover wall will press them here. Left and Right step along a rail,
/// Up and Down cross to the shelf above or below, and the target is scrolled
/// into view — which is also what makes a rail's off-screen tail reachable at
/// all without a pointer.</para>
///
/// <para><b>The rails are found in the visual tree rather than tracked.</b> The
/// shelves are an <c>ItemsControl</c> of templated content, so the controls
/// holding the cards do not exist until the template has been applied and are
/// replaced wholesale on every reload. Reading them at the moment of the key
/// press cannot go stale; a cached list would, silently, and the symptom would
/// be arrow keys that stop working after a refresh.</para>
/// </summary>
public partial class FeedView : UserControl
{
    public FeedView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Answers an arrow key over the feed. Returns true when it moved focus, so
    /// the window can mark the key handled and nothing downstream — the
    /// library's own selection walk, in particular — also acts on it.
    /// </summary>
    public bool HandleNavigationKey(KeyEventArgs e)
    {
        var (alongRail, acrossRails) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };

        if (alongRail == 0 && acrossRails == 0)
        {
            return false;
        }

        var rails = Rails();
        if (rails.Count == 0)
        {
            return false;
        }

        var (rail, index) = Locate(rails);

        // Nothing focused yet: the first arrow key lands on the first card
        // rather than doing nothing, which is what a user pressing it is asking
        // for. Same rule the wall follows on its first key press.
        if (rail < 0)
        {
            return Take(rails[0][0]);
        }

        if (acrossRails != 0)
        {
            var target = rail + acrossRails;
            if (target < 0 || target >= rails.Count)
            {
                return false;
            }

            // Hold the column across the jump where the shelf is long enough,
            // and land on its last card where it is not — a short shelf must
            // absorb the move rather than refuse it.
            return Take(rails[target][Math.Min(index, rails[target].Count - 1)]);
        }

        var next = index + alongRail;
        return next >= 0 && next < rails[rail].Count && Take(rails[rail][next]);
    }

    /// <summary>The cards, grouped by the rail they are on, in presentation order.</summary>
    private List<List<FeedCardView>> Rails()
        => this.GetVisualDescendants()
            .OfType<ItemsControl>()
            .Where(items => items.Name == "RailItems")
            .Select(items => items.GetVisualDescendants().OfType<FeedCardView>().ToList())
            .Where(cards => cards.Count > 0)
            .ToList();

    /// <summary>Where focus currently is, as (rail, index), or (-1, -1) when it is not on a card.</summary>
    private (int Rail, int Index) Locate(List<List<FeedCardView>> rails)
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

        for (var rail = 0; rail < rails.Count; rail++)
        {
            var index = rails[rail].IndexOf(card);
            if (index >= 0)
            {
                return (rail, index);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Moves focus to a card and brings it on screen — both axes, since the move
    /// can be along a rail that is scrolled off to the right or down to a shelf
    /// below the fold.
    /// </summary>
    private static bool Take(FeedCardView card)
    {
        card.TakeFocus();
        card.BringIntoView();
        return true;
    }
}
