using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards for the command bar's density slider and the list view's
/// per-row cover in MainWindow.axaml. There is no headless Avalonia renderer
/// in this project, so these tests hold what the markup declares rather than
/// what a rendered frame draws. Companion to <see cref="StoreChipLayoutTests"/>,
/// which guards the same window's chip layout.
/// </summary>
public sealed class LibraryChromeTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    // ══ The density slider (TASK-100) ════════════════════════════════════════

    // The slider is labelled DENSITY but used to bind TileWidth directly
    // over 108-200, so dragging right made tiles bigger and fitted fewer
    // games -- the opposite of what the label promises. Density is TileWidth
    // reflected about the midpoint of the range, so the slider's own
    // left-to-right value grows as the tiles narrow. Rebinding Value to
    // TileWidth would reinstate the bug and change nothing visible in the
    // markup except one word, which is exactly the kind of regression a
    // source guard is for.
    [Fact]
    public void The_density_slider_binds_the_mirrored_value_over_the_view_models_own_range()
    {
        var slider = Load("src/Winnow.App/Views/MainWindow.axaml")
            .Descendants(Avalonia + "Slider")
            .Single(s => s.Attribute("Name")?.Value == "DensitySlider");

        Assert.Equal("{Binding Library.Density, Mode=TwoWay}", slider.Attribute("Value")?.Value);
        Assert.Equal(
            "{x:Static vm:LibraryViewModel.MinimumTileWidth}",
            slider.Attribute("Minimum")?.Value);
        Assert.Equal(
            "{x:Static vm:LibraryViewModel.MaximumTileWidth}",
            slider.Attribute("Maximum")?.Value);
    }

    // The mirror is Minimum + Maximum - TileWidth, so it is only the
    // identity the view model claims for as long as the range the markup
    // declares is the range the mirror is taken over. The two constants
    // are bound into the markup with x:Static precisely so they cannot
    // drift; this pins their values (108 and 200, the 108x162 and 200x300
    // tile ends of design-system.md section 4).
    [Fact]
    public void The_mirror_is_taken_over_the_range_the_slider_declares()
    {
        Assert.Equal(108d, LibraryViewModel.MinimumTileWidth);
        Assert.Equal(200d, LibraryViewModel.MaximumTileWidth);
        Assert.True(LibraryViewModel.MinimumTileWidth < LibraryViewModel.MaximumTileWidth);
    }

    // ══ The list row's cover (TASK-94) ═══════════════════════════════════════

    // A 24x36 cover sits on the left edge of every list row. The column
    // holding it is a fixed width (32px: the 24px cover plus 4px either
    // side). That fixed width is what keeps a row with a cover and a row
    // without identical in height and in where every later column starts;
    // a star or Auto column would let the absence of art shift the row.
    [Fact]
    public void Every_list_row_draws_its_cover_in_a_fixed_column()
    {
        var window = Load("src/Winnow.App/Views/MainWindow.axaml");

        var cover = Assert.Single(
            window.Descendants(), e => e.Name.LocalName == "RowCoverView");
        var column = int.Parse(
            cover.Attribute("Grid.Column")!.Value, CultureInfo.InvariantCulture);

        var grid = cover.Ancestors(Avalonia + "Grid").First();
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');

        Assert.True(
            double.TryParse(columns[column], CultureInfo.InvariantCulture, out var width),
            $"The list row's art column is '{columns[column]}'; a star or Auto column lets a "
            + "row without a cover sit at a different width from a row with one.");
        Assert.True(width > 0);
    }

    // The list is two grids -- the column-header strip and the row
    // template -- that declare their columns separately and must stay
    // identical, or the headers stop naming the columns underneath them.
    // Inserting the art column meant editing both and shifting every
    // Grid.Column after it; this catches the next person who edits one
    // and forgets the other.
    [Fact]
    public void The_list_header_and_the_list_row_declare_the_same_columns()
    {
        var definitions = Load("src/Winnow.App/Views/MainWindow.axaml")
            .Descendants(Avalonia + "Grid")
            .Select(g => g.Attribute("ColumnDefinitions")?.Value)
            .Where(v => v is not null && v.Contains(",136,", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, definitions.Count);
        Assert.Equal(definitions[0], definitions[1]);
    }

    // ══ Loading ══════════════════════════════════════════════════════════════

    private static XElement Load(string relativePath)
    {
        var root = typeof(LibraryChromeTests).Assembly
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
