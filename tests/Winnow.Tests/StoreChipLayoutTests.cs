using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards for the store-chip layout. There is no headless Avalonia
/// renderer in this project, so these tests hold the markup's own attributes
/// rather than
/// measuring a rendered frame. They are guards on what the markup declares,
/// not on what a renderer draws.
/// </summary>
public sealed class StoreChipLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";


    // ══ The measured figures ═════════════════════════════════════════════════
    //
    // Every figure below was measured against the bundled OFL faces (Plus
    // Jakarta Sans, IBM Plex Mono) at the exact sizes, weights, letter-spacing
    // and padding the markup sets, not estimated.

    /// <summary>STEAM 44.0 + 4 + EPIC 34.8 + 4 + GOG 36.3.</summary>
    private const double ThreeChips = 123.1;

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
            .Where(v => v is not null && v.StartsWith("2,32,18,*,", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, grids.Count);
        foreach (var definition in grids)
        {
            var store = double.Parse(definition!.Split(',')[4], CultureInfo.InvariantCulture);
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
