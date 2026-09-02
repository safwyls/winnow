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

    /// <summary>Card border 2+2 and padding 20+20.</summary>
    private const double CardChrome = 44;

    /// <summary>The primary member's capsule (§6).</summary>
    private const double CoverColumn = 200;

    /// <summary>The gutter between that capsule and the roster.</summary>
    private const double CoverGutter = 28;

    /// <summary>
    /// Roster row minimum: member chrome 30, checkbox 16 + 14, chip cover 64,
    /// two 14px margins, the condensed TITLE/YEAR/PUBLISHER evidence line at
    /// 271.7 (it does not wrap), and the Keep this title radio at 102.3.
    /// </summary>
    private const double RosterRowMinimum = 30 + 16 + 14 + 64 + 14 + 271.7 + 14 + 102.3;

    /// <summary>What the markup sets, and what this file exists to hold.</summary>
    private const double CardMaxWidth = 840;

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

    // ══ The Same Game card ═══════════════════════════════════════════════════

    // The roster density is the one that sets the ceiling: it needs more
    // width than a two-member card does, because a roster row carries its
    // evidence on one non-wrapping line instead of comparing two covers.
    //
    // The measure is no longer set on the card. It sat there alone, so on a
    // wide window the header's right-aligned count stood hundreds of pixels
    // from the cards it counted; it is now one content column that the header,
    // the count, the outcome report, the card list, the empty state and the
    // history log all sit in.
    [Fact]
    public void The_same_game_screen_holds_one_measured_content_column()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");
        var style = view
            .Descendants(Avalonia + "Style")
            .Single(s => s.Attribute("Selector")?.Value == ":is(Control).measure");

        var setters = style
            .Elements(Avalonia + "Setter")
            .ToDictionary(
                e => e.Attribute("Property")!.Value,
                e => e.Attribute("Value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        Assert.Equal("Center", setters["HorizontalAlignment"]);

        var maxWidth = double.Parse(setters["MaxWidth"], CultureInfo.InvariantCulture);
        Assert.Equal(CardMaxWidth, maxWidth);

        // The roster is the density that sets the ceiling.
        var roster = CardChrome + CoverColumn + CoverGutter + RosterRowMinimum;
        Assert.True(
            maxWidth >= roster,
            $"The column is capped at {maxWidth}px and the roster row needs {roster}px.");

        // The card fills the column rather than setting one of its own.
        var card = view
            .Descendants(Avalonia + "Style")
            .Single(s => s.Attribute("Selector")?.Value == "Border.card")
            .Elements(Avalonia + "Setter")
            .Select(e => e.Attribute("Property")!.Value)
            .ToList();

        Assert.DoesNotContain("MaxWidth", card);
        Assert.DoesNotContain("HorizontalAlignment", card);
    }

    // Everything the user reads down the screen takes that column: the segment
    // strip's content, each surface's header (which carries its count and its
    // outcome report), each card list, each empty state, and the history log.
    [Fact]
    public void Every_column_of_the_same_game_screen_takes_the_measure()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var carriers = view
            .Descendants()
            .Where(e => e.Attribute("Classes")?.Value.Split(' ').Contains("measure") == true)
            .ToList();

        Assert.Equal(8, carriers.Count);

        // The count, the outcome report and the empty state are inside it, not
        // beside it.
        foreach (var path in new[]
        {
            "{Binding PendingCountText}",
            "{Binding ExpansionCountText}",
            "{Binding EmptyMessage}",
            "{Binding ExpansionsEmptyMessage}",
        })
        {
            var element = Assert.Single(
                view.Descendants(Avalonia + "TextBlock"),
                t => t.Attribute("Text")?.Value == path);
            Assert.True(
                InsideTheMeasure(element),
                $"{path} is drawn outside the content column.");
        }

        foreach (var report in view
            .Descendants(Avalonia + "ContentControl")
            .Where(c => c.Attribute("IsVisible")?.Value.Contains("Report", StringComparison.Ordinal) == true))
        {
            Assert.True(
                InsideTheMeasure(report),
                "An outcome report is drawn outside the content column.");
        }
    }

    // Both the primary column and the member row must bind StoreChips. Placement
    // differs (own line vs. leading the metadata line) but the fact is present
    // at every member of every card.
    [Fact]
    public void Both_same_game_densities_draw_the_store()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var key in new[] { "MergeMemberTemplate", "MergeRosterRowTemplate" })
        {
            var template = view
                .Descendants(Avalonia + "DataTemplate")
                .Single(t => t.Attribute(Xaml + "Key")?.Value == key);

            Assert.Contains(template.Descendants(), e => Binds("StoreChips")(e));
        }
    }

    // ══ The expansion roster row ═════════════════════════════════════════════

    /// <summary>Member border 1+1 and padding 14+14.</summary>
    private const double MemberChrome = 30;

    /// <summary>The include checkbox and the 14px margin after it.</summary>
    private const double IncludeControl = 16 + 14;

    /// <summary>The pack chip and the 14px margin between it and the text column.</summary>
    private const double PackChip = 64 + 14;

    // The row read "...PUBLISHER SAME TITL" and clipped at the card edge. A
    // horizontal StackPanel measures children with unbounded width in the
    // stacking direction, so no width could ever have made the line fit. The
    // merge roster is built identically and has never overflowed because its
    // values are short numbers; this line carries one free-text value, the
    // suffix.
    //
    // A WrapPanel measures each child against the line being placed, so facts
    // move to a second line instead of off the card. The suffix is also
    // capped and trimmed, so no single group can exceed the column regardless
    // of title length, which is the part a wrap alone cannot fix.

    [Fact]
    public void The_expansion_evidence_wraps_inside_the_card()
    {
        var line = ExpansionEvidenceLine();

        Assert.Equal("WrapPanel", line.Name.LocalName);

        // Each label and the value it names travel together, so a wrap breaks
        // between facts and never leaves "PUBLISHER" on one line and "SAME" on
        // the next.
        var groups = line.Elements().ToList();
        Assert.Equal(4, groups.Count);
        foreach (var group in groups)
        {
            Assert.Equal("StackPanel", group.Name.LocalName);
            Assert.Equal("Horizontal", group.Attribute("Orientation")?.Value);
            Assert.Equal(2, group.Elements(Avalonia + "TextBlock").Count());
        }
    }

    [Fact]
    public void The_expansion_suffix_cannot_outgrow_the_column_it_sits_in()
    {
        var suffix = Assert.Single(
            ExpansionEvidenceLine().Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding SuffixText}");

        Assert.Equal("CharacterEllipsis", suffix.Attribute("TextTrimming")?.Value);

        var cap = double.Parse(suffix.Attribute("MaxWidth")!.Value, CultureInfo.InvariantCulture);

        // The relation word is the other Auto column competing for the row, so
        // it is capped too and its cap is charged against the text column here.
        var relation = Assert.Single(
            ExpansionRosterRow().Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding RelationText}");
        Assert.Equal("CharacterEllipsis", relation.Attribute("TextTrimming")?.Value);
        var relationCap =
            double.Parse(relation.Attribute("MaxWidth")!.Value, CultureInfo.InvariantCulture)
            + 14;

        var column = CardMaxWidth
            - CardChrome - MemberChrome - IncludeControl - PackChip - relationCap;

        // The suffix takes at most half of what the row has, which leaves its
        // own label at least as much again. Every other value on the line is
        // fixed vocabulary (a signed year, SAME/DIFFERENT, YES/NO).
        Assert.True(
            cap <= column / 2,
            $"The suffix may grow to {cap}px inside a {column}px column, which leaves its "
            + "EXTENDS BY label less room than the value it names.");
    }

    private static XElement ExpansionRosterRow()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");
        return view
            .Descendants(Avalonia + "DataTemplate")
            .Single(t => t.Attribute(Xaml + "Key")?.Value == "ExpansionRosterRowTemplate");
    }

    /// <summary>
    /// The innermost element holding the whole evidence line: several
    /// ancestors contain it, and the one under test is the one that lays it
    /// out.
    /// </summary>
    private static XElement ExpansionEvidenceLine()
        => ExpansionRosterRow()
            .Descendants()
            .Where(e => e.Descendants(Avalonia + "TextBlock")
                .Any(t => t.Attribute("Text")?.Value == "{Binding ExtendsLabel}")
                && e.Descendants(Avalonia + "TextBlock")
                    .Any(t => t.Attribute("Text")?.Value == "{Binding SeparatorText}"))
            .OrderBy(e => e.Descendants().Count())
            .First();

    private static bool InsideTheMeasure(XElement element)
    {
        for (var node = element.Parent; node is not null; node = node.Parent)
        {
            if (node.Attribute("Classes")?.Value.Split(' ').Contains("measure") == true)
            {
                return true;
            }
        }

        return false;
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
