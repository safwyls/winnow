using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards for the store-chip layout. There is no headless Avalonia
/// renderer in this project, so these tests hold the markup's own attributes
/// and arithmetic over <see cref="FeedGrid.GeometryFor"/> rather than
/// measuring a rendered frame. They are guards on what the markup declares,
/// not on what a renderer draws.
/// </summary>
public sealed class StoreChipLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // ══ The measured figures ═════════════════════════════════════════════════
    //
    // Every figure below was measured against the bundled OFL faces (Plus
    // Jakarta Sans, IBM Plex Mono) at the exact sizes, weights, letter-spacing
    // and padding the markup sets, not estimated.

    /// <summary>Install (66.1) + its 8px margin + Not interested (97.0) + Not now (64.7).</summary>
    private const double FeedActionLineWidth = 235.8;

    /// <summary>Card margin 14+14, cover 108, gutter 14.</summary>
    private const double FeedCardChrome = 150;

    /// <summary>STEAM 44.0 + 4 + EPIC 34.8 + 4 + GOG 36.3.</summary>
    private const double ThreeChips = 123.1;

    // ══ Feed card ════════════════════════════════════════════════════════════

    // The TASK-70.6 regression: the chip row replaced a Border that carried
    // Grid.Column="4" and did not carry one itself, so it defaulted to
    // column 0 and drew on top of Play/Install.
    [Fact]
    public void Every_child_of_the_feed_action_line_declares_its_column()
    {
        var grid = FeedActionGrid();

        foreach (var child in grid.Elements())
        {
            var column = child.Attribute("Grid.Column")?.Value;
            Assert.False(
                string.IsNullOrEmpty(column),
                $"<{child.Name.LocalName}> in the feed card's action line declares no Grid.Column, "
                + "so it draws in column 0 on top of Play/Install.");
        }
    }

    // The chips moved from the action line to the title block, where they
    // have the full content width rather than sharing one line with three
    // controls.
    [Fact]
    public void The_feed_store_chips_are_not_in_the_action_line()
    {
        var grid = FeedActionGrid();
        Assert.DoesNotContain(grid.Descendants(), e => Binds("Tile.StoreChips")(e));

        var card = Load("src/Winnow.App/Views/FeedCardView.axaml");
        Assert.Contains(card.Descendants(), e => Binds("Tile.StoreChips")(e));
    }

    // At every width FeedGrid draws a card at, the content column must hold
    // both the action line and three chips. This is arithmetic over
    // GeometryFor, not a rendered frame; it verifies the budget the move
    // from the action line to the title block buys.
    [Fact]
    public void The_narrowest_feed_card_fits_its_action_line_and_three_chips()
    {
        var minimum = FeedGridMinItemWidth();

        for (var width = 200d; width <= 4000d; width += 1d)
        {
            var (_, itemWidth) = FeedGrid.GeometryFor(width, minimum, 14);
            if (itemWidth < minimum)
            {
                // A pane narrower than one whole card. Nothing to assert about
                // a card that was never given its own width.
                continue;
            }

            var content = itemWidth - FeedCardChrome;
            Assert.True(
                content >= FeedActionLineWidth,
                $"At {width}px the card is {itemWidth}px and leaves {content}px, under the "
                + $"{FeedActionLineWidth}px the action line measures.");
            Assert.True(
                content >= ThreeChips,
                $"At {width}px the card leaves {content}px, under the {ThreeChips}px three "
                + "store chips measure.");
        }
    }

    // ══ The list column ══════════════════════════════════════════════════════

    // The list's store column was 112px, which held two chips (82.8px) but
    // not three (123.1px); a game owned on Steam, Epic and GOG overflowed
    // into the PLAYTIME column. Widened to 136px in both the header and the
    // row grids.
    [Fact]
    public void The_list_store_column_holds_three_chips()
    {
        var window = Load("src/Winnow.App/Views/MainWindow.axaml");
        var grids = window
            .Descendants(Avalonia + "Grid")
            .Select(g => g.Attribute("ColumnDefinitions")?.Value)
            .Where(v => v is not null && v.StartsWith("2,18,*,", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, grids.Count);
        foreach (var definition in grids)
        {
            var store = double.Parse(definition!.Split(',')[3], CultureInfo.InvariantCulture);
            Assert.True(
                store >= ThreeChips + 8,
                $"The list's store column is {store}px; three chips plus their 8px margin "
                + $"measure {ThreeChips + 8}px.");
        }
    }

    // ══ The Merges row ═══════════════════════════════════════════════════════

    // Every candidate row on the Merges screen states its stores: a row is a
    // work, so an entry owned on two stores is one row wearing two chips, and
    // the store is the fact that decides whether a pair is one game on two
    // storefronts.
    [Fact]
    public void Every_merges_row_draws_the_store()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var row = Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute("DataType")?.Value == "vm:MergeRowViewModel");

        var chips = row.Descendants().Single(Binds("StoreChips"));

        // Three chips (123.1px) must fit the row: the column is Auto and the
        // title column beside it is the one that gives way.
        var grid = row.Descendants(Avalonia + "Grid").First();
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');
        var chipColumn = int.Parse(chips.Attribute("Grid.Column")!.Value, CultureInfo.InvariantCulture);
        Assert.Equal("Auto", columns[chipColumn]);
        Assert.Contains("*", columns);
    }

    // ══ Loading ══════════════════════════════════════════════════════════════

    private static Func<XElement, bool> Binds(string path)
        => element => element.Attribute("ItemsSource")?.Value == $"{{Binding {path}}}";

    private static XElement FeedActionGrid()
    {
        var card = Load("src/Winnow.App/Views/FeedCardView.axaml");
        return card
            .Descendants(Avalonia + "Grid")
            .Single(g => g.Attribute("IsVisible")?.Value == "{Binding ShowActions}");
    }

    /// <summary>The FeedGrid.MinItemWidth the feed's own markup sets.</summary>
    private static double FeedGridMinItemWidth()
    {
        var feed = Load("src/Winnow.App/Views/FeedView.axaml");
        var grid = feed.Descendants().Single(e => e.Name.LocalName == "FeedGrid");
        return double.Parse(grid.Attribute("MinItemWidth")!.Value, CultureInfo.InvariantCulture);
    }

    private static XElement Load(string relativePath)
    {
        var root = typeof(StoreChipLayoutTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        Assert.False(
            string.IsNullOrWhiteSpace(root),
            "The test assembly carries no RepositoryRoot metadata, so the markup cannot be "
            + "read. See Winnow.Tests.csproj.");

        var path = Path.Combine(root!, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The markup was not found at '{path}'.");

        return XDocument.Load(path).Root!;
    }
}
