using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards for the IGDB candidate-row layout. There is no headless
/// Avalonia renderer in this project, so these tests hold the markup's
/// own attributes rather than measuring a rendered frame. They are guards
/// on what the markup declares, not on what a renderer draws.
/// </summary>
public sealed class IgdbCandidateRowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>The two views that draw an IGDB candidate row from the same view model.</summary>
    public static TheoryData<string> CandidateRowViews => new()
    {
        "src/Winnow.App/Views/GameDetailsView.axaml",
        "src/Winnow.App/Views/LibrarySettingsView.axaml",
    };

    // A child that omits Grid.Column defaults to column 0 and draws on
    // top of the cover, the same defect TASK-70.8 fixed on the feed card.
    [Theory]
    [MemberData(nameof(CandidateRowViews))]
    public void Every_child_of_the_candidate_row_declares_its_column(string view)
    {
        var grid = CandidateRowGrid(view);

        foreach (var child in grid.Elements().Where(e => !e.Name.LocalName.Contains('.')))
        {
            var column = child.Attribute("Grid.Column")?.Value;
            Assert.False(
                string.IsNullOrEmpty(column),
                $"<{child.Name.LocalName}> in {view}'s candidate row declares no Grid.Column, "
                + "so it draws in column 0 on top of the cover.");
        }
    }

    // The text column is star so the Grid measures it at the allocated
    // width and TextTrimming engages. The button is trailing Auto so it
    // never competes with the text for space.
    [Theory]
    [MemberData(nameof(CandidateRowViews))]
    public void The_candidate_row_gives_the_text_the_star_column_and_the_button_the_last(string view)
    {
        var grid = CandidateRowGrid(view);
        var columns = grid.Attribute("ColumnDefinitions")!.Value.Split(',');

        Assert.Equal(3, columns.Length);
        Assert.Equal("34", columns[0]);
        Assert.Equal("*", columns[1]);
        Assert.Equal("Auto", columns[2]);

        var button = Assert.Single(grid.Elements(Avalonia + "Button"));
        Assert.Equal("2", button.Attribute("Grid.Column")?.Value);

        var text = Assert.Single(grid.Elements(Avalonia + "StackPanel"));
        Assert.Equal("1", text.Attribute("Grid.Column")?.Value);
    }

    // TASK-118: a horizontal StackPanel measures its children with
    // infinite width, so TextTrimming on the platforms never engaged
    // and the text ran under the assign button past the scrollbar.
    [Theory]
    [MemberData(nameof(CandidateRowViews))]
    public void The_detail_line_is_a_grid_and_not_a_horizontal_stack(string view)
    {
        var line = DetailLine(view);

        Assert.True(
            line.Name == Avalonia + "Grid",
            $"{view}'s candidate detail line is a <{line.Name.LocalName}>. A horizontal "
            + "StackPanel measures its children with infinite width, so TextTrimming never "
            + "engages and the platforms run under the assign button.");

        var columns = line.Attribute("ColumnDefinitions")!.Value.Split(',');
        Assert.Equal("Auto", columns[0]);
        Assert.Equal("*", columns[^1]);
    }

    // The platforms must trim inside the star column and carry the full
    // list as a tooltip, because a trimmed platform list is how a user
    // tells one edition from another.
    [Theory]
    [MemberData(nameof(CandidateRowViews))]
    public void The_platforms_trim_inside_the_star_column_and_keep_their_full_value(string view)
    {
        var line = DetailLine(view);
        var columns = line.Attribute("ColumnDefinitions")!.Value.Split(',');
        var star = Array.FindIndex(columns, c => c == "*");

        var platforms = Assert.Single(
            line.Elements(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding PlatformsText}");

        Assert.Equal(
            star,
            int.Parse(
                platforms.Attribute("Grid.Column")?.Value ?? "0",
                CultureInfo.InvariantCulture));

        Assert.Equal("CharacterEllipsis", platforms.Attribute("TextTrimming")?.Value);
        Assert.Equal("{Binding PlatformsTooltip}", platforms.Attribute("ToolTip.Tip")?.Value);

        // A trimmed line must stay one line: wrapping would make a row with a
        // long platform list taller than a row with a short one.
        Assert.Null(platforms.Attribute("TextWrapping"));

        var year = Assert.Single(
            line.Elements(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding YearText}");
        Assert.Equal("0", year.Attribute("Grid.Column")?.Value);
    }

    // ══ Loading ══════════════════════════════════════════════════════════════

    private static XElement CandidateRowGrid(string view)
    {
        var template = Assert.Single(
            Load(view).Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute("DataType")?.Value == "vm:IgdbCandidateViewModel");

        return template.Descendants(Avalonia + "Grid").First();
    }

    private static XElement DetailLine(string view)
        => Assert.Single(
            CandidateRowGrid(view).Descendants(),
            e => e.Attribute("IsVisible")?.Value == "{Binding HasDetailLine}");

    private static XElement Load(string relativePath)
    {
        var root = typeof(IgdbCandidateRowLayoutTests).Assembly
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
