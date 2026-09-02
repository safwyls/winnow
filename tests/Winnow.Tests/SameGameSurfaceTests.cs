using System.Reflection;
using System.Xml.Linq;
using Winnow.App.ViewModels;
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

        var notes = view
            .Descendants(Avalonia + "ContentControl")
            .Where(c => c.Attribute("IsVisible")?.Value.Contains("Report", StringComparison.Ordinal) == true)
            .ToList();

        var bound = notes.Select(c => c.Attribute("IsVisible")!.Value).ToList();

        Assert.Equal(3, bound.Count);
        Assert.Equal(3, bound.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("{Binding HasReport}", bound);

        // One note, shown by whichever surface raised the outcome. It was
        // written out three times identically.
        Assert.All(
            notes,
            c => Assert.Equal(
                "{StaticResource ReportNoteTemplate}", c.Attribute("ContentTemplate")?.Value));

        Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute(Xaml + "Key")?.Value == "ReportNoteTemplate");
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
            p => p.Attribute("AutomationProperties.Name")?.Value
                == "{Binding Evidence.SummaryText}");

        Assert.Equal(
            "{Binding ShowCondensedEvidence}", evidence.Attribute("IsVisible")?.Value);
    }

    // ── One card, one layout ════════════════════════════════════════════════

    // The card held two Grids switched on IsPair and two member templates, one
    // of which served as both a pair side and a roster column. Nothing on the
    // screen switches a layout on the member count any more; what varies is
    // inside the row, and the row asks the member.

    [Fact]
    public void No_layout_on_the_screen_switches_on_the_member_count()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var element in view.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                Assert.DoesNotContain("IsPair", attribute.Value, StringComparison.Ordinal);
            }
        }

        // One member row template, and its cover is sized by the member.
        var row = Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute(Xaml + "Key")?.Value == "MergeRosterRowTemplate");

        var cover = Assert.Single(
            row.Descendants(Avalonia + "Border"),
            b => b.Attribute("Width")?.Value == "{Binding CoverWidth}");
        Assert.Equal("{Binding CoverHeight}", cover.Attribute("Height")?.Value);

        // The include control is drawn only when it means something.
        var checkBox = Assert.Single(row.Descendants(Avalonia + "CheckBox"));
        Assert.Equal("{Binding ShowIncludeControl}", checkBox.Attribute("IsVisible")?.Value);

        // One signal template, shared by the open diff and the disclosure.
        Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute(Xaml + "Key")?.Value == "MergeSignalTemplate");
    }

    // The scorer's signed points are gone with the column that printed them,
    // and so is the per-row restatement of the group's own score.

    [Fact]
    public void The_evidence_shows_no_arithmetic()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var forbidden in new[]
        {
            "ContributionText", "IsForMatch", "IsAgainstMatch", "BestScoreText",
        })
        {
            Assert.DoesNotContain(
                view.Descendants().SelectMany(e => e.Attributes()),
                a => a.Value.Contains(forbidden, StringComparison.Ordinal));
        }

        foreach (var selector in new[]
        {
            "TextBlock.contribution", "TextBlock.contribution.pos", "TextBlock.contribution.neg",
        })
        {
            Assert.DoesNotContain(
                view.Descendants(Avalonia + "Style"),
                s => s.Attribute("Selector")?.Value == selector);
        }

        // The confidence figure stays: it is what sorts the queue.
        Assert.Contains(
            view.Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding ScoreText}");
    }

    // Entry numbers are database ids. §10.5 rejected showing those and the
    // store chips do the disambiguating job they were doing.

    [Fact]
    public void No_member_on_the_screen_shows_its_entry_numbers()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var forbidden in new[] { "ReleasesText", "ReleaseText" })
        {
            Assert.DoesNotContain(
                view.Descendants().SelectMany(e => e.Attributes()),
                a => a.Value.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    // ── The matcher's band, named for what it is ═════════════════════════════

    // TOP OF QUEUE was wrong twice: it binds IsPriority, which is the matcher's
    // top confidence band and not a position, so several cards carry it at once
    // and the queue is already sorted by score. Its tooltip was the
    // over-explanatory blurb notes.md asks us to drop.

    [Fact]
    public void The_confidence_band_is_named_from_copy_and_carries_no_tooltip()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var band = Assert.Single(
            view.Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("IsVisible")?.Value == "{Binding IsPriority}");

        Assert.Equal("{Binding PriorityBandLabel}", band.Attribute("Text")?.Value);
        Assert.Null(band.Attribute("ToolTip.Tip"));

        Assert.DoesNotContain(
            "TOP OF QUEUE",
            File.ReadAllText(Path("src/Winnow.App/Views/MergeQueueView.axaml")),
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Every string comes from the copy file ════════════════════════════════

    [Fact]
    public void Every_user_facing_string_on_the_screen_comes_from_copy()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        foreach (var element in view.Descendants())
        {
            foreach (var name in new[] { "Text", "Content", "ToolTip.Tip" })
            {
                if (element.Attribute(name)?.Value is not { Length: > 0 } value)
                {
                    continue;
                }

                if (value.StartsWith('{') || value == "·")
                {
                    continue;
                }

                Assert.Fail(
                    $"<{element.Name.LocalName} {name}=\"{value}\"> is a literal. Every "
                    + "user-facing string on this screen lives in MergeCopy or ExpansionCopy.");
            }
        }
    }

    // ── Undo, not retract ═══════════════════════════════════════════════════

    // "Retract" was engineering vocabulary in the interface. Undo is what a user
    // calls it, and the rename covers the copy, the tooltips and the automation
    // names. The repository API keeps its own name: that is another layer.

    [Fact]
    public void No_user_facing_string_says_retract()
    {
        foreach (var copy in new[] { typeof(MergeCopy), typeof(ExpansionCopy) })
        {
            foreach (var field in copy.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not string value)
                {
                    continue;
                }

                Assert.DoesNotContain("retract", value, StringComparison.OrdinalIgnoreCase);
            }
        }

        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");
        foreach (var attribute in view.Descendants().SelectMany(e => e.Attributes()))
        {
            Assert.DoesNotContain("Retract", attribute.Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── The expansion row states its relation ═══════════════════════════════

    [Fact]
    public void The_expansion_row_draws_the_relations_own_word()
    {
        var view = Load("src/Winnow.App/Views/MergeQueueView.axaml");

        var row = Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute(Xaml + "Key")?.Value == "ExpansionRosterRowTemplate");

        var relation = Assert.Single(
            row.Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding RelationText}");

        Assert.Equal("{Binding HasRelation}", relation.Attribute("IsVisible")?.Value);
    }

    // ── The deleted members are unreferenced ════════════════════════════════

    // The pass named each of these as dead, or the user's decisions retired it.
    // A member nothing binds and nothing calls is indistinguishable from one
    // that does not exist, and this screen has carried several of them.

    [Fact]
    public void Every_member_the_pass_deleted_is_gone()
    {
        foreach (var (type, member) in new (Type, string)[]
        {
            (typeof(MergeGroupMemberViewModel), "IncludeControlText"),
            (typeof(MergeGroupMemberViewModel), "BestScoreText"),
            (typeof(MergeGroupMemberViewModel), "ReleasesText"),
            (typeof(MergeGroupMemberViewModel), "ChipHeight"),
            (typeof(MergeSideViewModel), "NormalizedTitle"),
            (typeof(MergeSideViewModel), "HasPublisher"),
            (typeof(MergeSideViewModel), "ReleaseText"),
            (typeof(MergeQueueViewModel), "DifferentGamesTooltip"),
            (typeof(MergeQueueViewModel), "CoverHeight"),
            (typeof(MergeGroupViewModel), "Left"),
            (typeof(MergeGroupViewModel), "Right"),
            (typeof(MergeGroupViewModel), "Ordered"),
            (typeof(MergeGroupViewModel), "PairEdge"),
            (typeof(MergeGroupViewModel), "PairHasNoSignals"),
            (typeof(MergeGroupViewModel), "EffectLine"),
            (typeof(MergeGroupViewModel), "PrimaryLabel"),
            (typeof(MergeLinkHistoryRowViewModel), "ChildCountText"),
            (typeof(MergeLinkHistoryRowViewModel), "RetractedAtText"),
            (typeof(MergeSignalViewModel), "ContributionText"),
            (typeof(MergeSignalViewModel), "Contribution"),
            (typeof(MergeSignalViewModel), "IsForMatch"),
            (typeof(MergeSignalViewModel), "IsAgainstMatch"),
            (typeof(ExpansionGroupViewModel), "CoverWidth"),
            (typeof(ExpansionGroupViewModel), "CoverHeight"),
            (typeof(ExpansionGroupViewModel), "EffectLine"),
            (typeof(ExpansionMemberViewModel), "ChipWidth"),
            (typeof(ExpansionMemberViewModel), "ChipHeight"),
            (typeof(ExpansionMemberViewModel), "ReleasesText"),
        })
        {
            Assert.Null(type.GetMember(member).FirstOrDefault());
        }

        foreach (var (type, member) in new (Type, string)[]
        {
            (typeof(MergeCopy), "PrimaryLabel"),
            (typeof(MergeCopy), "LinkEffect"),
            (typeof(MergeCopy), "RetractedLabel"),
            (typeof(MergeCopy), "MemberAutomationFormat"),
            (typeof(MergeCopy), "MemberWithStoreAutomationFormat"),
            (typeof(ExpansionCopy), "GroupEffect"),
            (typeof(ExpansionCopy), "Retracted"),
        })
        {
            Assert.Null(type.GetMember(member).FirstOrDefault());
        }
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
