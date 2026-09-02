using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards over the Same Game markup and over <c>OnMergeQueueKeyDown</c>, in the
/// same "hold what the markup declares" spirit as <see cref="StoreChipLayoutTests"/>.
/// </summary>
public sealed class SameGameSurfaceTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    // ── Selection follows focus on BOTH card surfaces ════════════════════════

    // Both card lists take selection from focus; the expansion list declared no GotFocus.

    [Fact]
    public void Both_card_lists_take_selection_from_focus()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var source in new[] { "Groups", "ExpansionGroups" })
        {
            var list = Assert.Single(
                view.Descendants(Avalonia + "ItemsControl"),
                e => e.Attribute("ItemsSource")?.Value == $"{{Binding {source}}}");

            Assert.False(
                string.IsNullOrEmpty(list.Attribute("GotFocus")?.Value),
                $"The {source} list declares no GotFocus, so Tabbing into a card does not "
                + "select it and the shortcut answers whichever card the last load selected.");
        }
    }

    // Both card templates take selection from a click; the expansion card declared no PointerPressed, so its Volt selected edge was unreachable.

    [Fact]
    public void Both_card_templates_take_selection_from_a_click()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var type in new[] { "vm:MergeGroupViewModel", "vm:ExpansionGroupViewModel" })
        {
            var template = Assert.Single(
                view.Descendants(Avalonia + "DataTemplate"),
                t => t.Attribute("DataType")?.Value == type
                    && t.Attribute(Xaml + "Key") is null);

            var card = Assert.Single(
                template.Descendants(Avalonia + "Border"),
                b => b.Attribute("Classes")?.Value == "card");

            Assert.False(
                string.IsNullOrEmpty(card.Attribute("PointerPressed")?.Value),
                $"The {type} card declares no PointerPressed, so clicking a card does not "
                + "select it and its Volt selected edge is unreachable.");
        }
    }

    // The expansion key branch walks the cards with Up/Down.

    [Fact]
    public void The_expansion_key_branch_walks_the_cards()
    {
        var branch = ExpansionKeyBranch();

        foreach (var token in new[] { "Key.Up", "Key.Down", "MoveExpansionSelection" })
        {
            Assert.Contains(token, branch, StringComparison.Ordinal);
        }
    }

    // Every expansion shortcut acts on the selected card, never a list position.

    [Fact]
    public void Every_expansion_shortcut_acts_on_the_selected_card()
    {
        var branch = ExpansionKeyBranch();

        foreach (var line in branch.Split('\n'))
        {
            if (!line.Contains("Command.Execute(", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Contains("SelectedExpansionGroup", line, StringComparison.Ordinal);
        }
    }

    // ── The outcome report is scoped to its own surface ══════════════════════

    // Each surface's report note reads its own surface; three notes, three different bindings.

    [Fact]
    public void Each_surfaces_report_note_reads_its_own_surface()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var bound = view
            .Descendants(Avalonia + "Border")
            .Select(b => b.Attribute("IsVisible")?.Value)
            .Where(v => v is not null && v.Contains("Report", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, bound.Count);
        Assert.Equal(3, bound.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("{Binding HasReport}", bound);
    }

    // ── The rail reflects both surfaces ══════════════════════════════════════

    // The rail row counts both surfaces.

    [Fact]
    public void The_rail_row_counts_review_and_expansions()
    {
        var window = Load("src/Winnow.App/Views/MainWindow.axaml");

        var count = Assert.Single(
            window.Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value.StartsWith("{Binding MergeQueue.", StringComparison.Ordinal) == true);

        Assert.Equal("{Binding MergeQueue.OutstandingCountText}", count.Attribute("Text")!.Value);
        Assert.Equal("{Binding MergeQueue.HasOutstanding}", count.Attribute("IsVisible")!.Value);

        var row = Assert.Single(
            window.Descendants(Avalonia + "Button"),
            b => b.Attribute("Opacity")?.Value == "{Binding MergeQueue.RowOpacity}");
        Assert.NotNull(row);
    }

    // ── Copy and automation names ═══════════════════════════════════════════

    // The expansion templates used an ASCII full stop where every merge template uses a middle dot.

    [Fact]
    public void The_screen_separates_metadata_with_one_character()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var separators = view
            .Descendants(Avalonia + "TextBlock")
            .Select(t => t.Attribute("Text")?.Value)
            .Where(v => v is "." or "·")
            .ToList();

        Assert.NotEmpty(separators);
        Assert.All(separators, v => Assert.Equal("·", v));
    }

    // Six unlabelled TextBlocks announced as bare values; MergeEdgeViewModel.SummaryText exists for exactly this and was bound nowhere.

    [Fact]
    public void The_roster_evidence_line_announces_as_labelled_values()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var template = Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute(Xaml + "Key")?.Value == "MergeRosterRowTemplate");

        var evidence = Assert.Single(
            template.Descendants(Avalonia + "StackPanel"),
            p => p.Attribute("IsVisible")?.Value == "{Binding HasDirectEvidence}");

        Assert.Equal(
            "{Binding Evidence.SummaryText}",
            evidence.Attribute("AutomationProperties.Name")?.Value);
    }

    // ── Loading ══════════════════════════════════════════════════════════════

    /// <summary>The expansions arm of OnMergeQueueKeyDown, as source text.</summary>
    private static string ExpansionKeyBranch()
    {
        var source = File.ReadAllText(Path(("src/Winnow.App/Views/MainWindow.axaml.cs")));

        var start = source.IndexOf("if (queue.IsExpansionsVisible)", StringComparison.Ordinal);
        Assert.True(start >= 0, "OnMergeQueueKeyDown has no expansions branch.");

        var end = source.IndexOf("if (!queue.IsReviewVisible)", start, StringComparison.Ordinal);
        Assert.True(end > start, "The expansions branch does not end where it used to.");

        return source[start..end];
    }

    private static string Path(string relativePath)
    {
        var root = typeof(SameGameSurfaceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        Assert.False(
            string.IsNullOrWhiteSpace(root),
            "The test assembly carries no RepositoryRoot metadata, so the source cannot be read.");

        var path = System.IO.Path.Combine(
            root!, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The source was not found at '{path}'.");
        return path;
    }

    private static XElement Load(string relativePath) => XDocument.Load(Path(relativePath)).Root!;
}
