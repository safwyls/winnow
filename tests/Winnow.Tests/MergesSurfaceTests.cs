using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards over the Merges screen's markup, the shell that hosts it and
/// <c>OnMergeQueueKeyDown</c>, in the same "hold what the markup declares"
/// spirit as <see cref="StoreChipLayoutTests"/>: there is no headless Avalonia
/// renderer, so these read the XAML and the code-behind as text.
/// </summary>
public sealed class MergesSurfaceTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string MergeView = "src/Winnow.App/Views/MergeQueueView.axaml";

    private const string Window = "src/Winnow.App/Views/MainWindow.axaml";

    private const string WindowCode = "src/Winnow.App/Views/MainWindow.axaml.cs";

    private const string Controls = "src/Winnow.App/Themes/controls.axaml";

    private const string Tokens = "src/Winnow.App/Themes/tokens.axaml";

    /// <summary>
    /// The five composites that moved out of the two views and into
    /// controls.axaml the moment the Merges screen became their second wearer.
    /// </summary>
    private static readonly Regex SharedSelector = new(
        @"^(?:Button\.ctl|FlyoutPresenter\.sortmenu|Button\.sortitem|Border\.chip|Button\.chipx)(?![\w-])",
        RegexOptions.CultureInvariant);

    // ── The row answers the pointer and the keyboard ═════════════════════════

    // Hover fills the row and swaps the reason slot; a click promotes; focus
    // moves the cursor. All four are wired on the row itself, and the row is
    // focusable, or Tab walks past every row and S answers a card the focus
    // ring is not on.

    [Fact]
    public void The_row_template_takes_hover_click_and_focus()
    {
        var row = RowBorder();

        Assert.Equal("True", row.Attribute("Focusable")?.Value);

        foreach (var handler in new[] { "PointerEntered", "PointerExited", "PointerPressed", "GotFocus" })
        {
            Assert.False(
                string.IsNullOrEmpty(row.Attribute(handler)?.Value),
                $"The row declares no {handler}, so the pointer or the keyboard reaches a row "
                + "the screen does not notice.");
        }

        // The cursor's three states are classes on the row, driven by the row.
        Assert.Equal("{Binding IsHeader}", row.Attribute("Classes.header")?.Value);
        Assert.Equal("{Binding IsHovered}", row.Attribute("Classes.hover")?.Value);
        Assert.Equal("{Binding CanPromote}", row.Attribute("Classes.promotable")?.Value);
    }

    // ── Automation names identify the group, never the verb alone (§8) ═══════

    [Fact]
    public void Every_answer_on_the_card_carries_an_automation_name()
    {
        var view = Load(MergeView);

        foreach (var command in new[] { "SameGameCommand", "DifferentGamesCommand", "SeparateCommand" })
        {
            var button = Assert.Single(
                view.Descendants(Avalonia + "Button"),
                b => b.Attribute("Command")?.Value.Contains(command, StringComparison.Ordinal) == true);

            var name = button.Attribute("AutomationProperties.Name")?.Value;
            Assert.True(
                name?.StartsWith("{Binding ", StringComparison.Ordinal) == true,
                $"The {command} button carries no bound automation name, so a screen reader hears "
                + "the verb with nothing joining it to the group.");

            // The label is copy, reached through the screen's view model.
            Assert.StartsWith("{Binding ", button.Attribute("Content")?.Value, StringComparison.Ordinal);
            Assert.StartsWith("{Binding ", button.Attribute("ToolTip.Tip")?.Value, StringComparison.Ordinal);

            // The card is the parameter: the shortcut and the button answer the same thing.
            Assert.Equal("{Binding}", button.Attribute("CommandParameter")?.Value);
        }
    }

    // ── The sort button carries the library's menu ═══════════════════════════

    [Fact]
    public void The_sort_button_carries_the_sort_menu()
    {
        var view = Load(MergeView);

        var button = Assert.Single(
            view.Descendants(Avalonia + "Button"),
            b => b.Attribute("Name")?.Value == "SortButton");

        Assert.Contains("ctl", button.Attribute("Classes")!.Value.Split(' '), StringComparer.Ordinal);
        Assert.StartsWith(
            "{Binding ", button.Attribute("AutomationProperties.Name")?.Value, StringComparison.Ordinal);

        var flyout = Assert.Single(
            Assert.Single(button.Elements(Avalonia + "Button.Flyout")).Elements(Avalonia + "Flyout"));

        Assert.Equal("sortmenu", flyout.Attribute("FlyoutPresenterClasses")?.Value);

        // Its rows are the library's sort rows, and each one closes the menu.
        var template = Assert.Single(
            flyout.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute("DataType")?.Value == "vm:MergeSortOptionViewModel");

        var item = Assert.Single(template.Elements(Avalonia + "Button"));
        Assert.Equal("sortitem", item.Attribute("Classes")?.Value);
        Assert.False(string.IsNullOrEmpty(item.Attribute("Click")?.Value));
    }

    // ── The cut bar's segments select a kind ═════════════════════════════════

    [Fact]
    public void Every_kind_segment_selects_its_kind()
    {
        var view = Load(MergeView);

        var template = Assert.Single(
            view.Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute("DataType")?.Value == "vm:MergeKindOptionViewModel");

        var segment = Assert.Single(template.Elements(Avalonia + "Button"));

        Assert.Equal("seg tab", segment.Attribute("Classes")?.Value);
        Assert.Equal("{Binding IsSelected}", segment.Attribute("Classes.on")?.Value);
        Assert.Contains(
            "SelectKindCommand", segment.Attribute("Command")?.Value, StringComparison.Ordinal);
        Assert.Equal("{Binding}", segment.Attribute("CommandParameter")?.Value);
    }

    // ── Every string comes from the copy file ════════════════════════════════

    // A literal in the markup is a string nobody can read beside the others.
    // Bindings are fine; anything else must be one of MergeCopy's constants.

    [Fact]
    public void Every_user_facing_string_on_the_screen_comes_from_copy()
    {
        var copy = CopyStrings(typeof(MergeCopy));
        Assert.NotEmpty(copy);

        var view = Load(MergeView);

        foreach (var element in view.Descendants())
        {
            foreach (var name in new[] { "Text", "Content", "ToolTip.Tip", "AutomationProperties.Name", "Watermark" })
            {
                if (element.Attribute(name)?.Value is not { Length: > 0 } value)
                {
                    continue;
                }

                if (value.StartsWith('{') || copy.Contains(value))
                {
                    continue;
                }

                Assert.Fail(
                    $"<{element.Name.LocalName} {name}=\"{value}\"> is a literal that is not in "
                    + "MergeCopy. Every user-facing string on this screen lives there.");
            }
        }
    }

    // ── Undo, not retract; Same game, not Merge records; no Cancel ═══════════

    // "Retract" is engineering vocabulary and the repository keeps it. §7 has
    // the affirmative answer as "Same game", never "Merge records", and the
    // negative answer is the other half of the answer, not a cancel.

    [Fact]
    public void No_user_facing_string_says_retract_merge_records_or_cancel()
    {
        var forbidden = new[] { "retract", "Merge records", "Cancel" };

        foreach (var value in CopyStrings(typeof(MergeCopy)))
        {
            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        var attributes = Load(MergeView).Descendants().SelectMany(e => e.Attributes())
            .Concat(Dock().Descendants().SelectMany(e => e.Attributes()))
            .Concat(Dock().Attributes());

        foreach (var attribute in attributes)
        {
            foreach (var word in forbidden)
            {
                Assert.DoesNotContain(word, attribute.Value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ── The screen's two transitions ═════════════════════════════════════════

    // The reason slot cross-fades its ink in 120ms when a hovered row's
    // detail replaces the reason; the row restores its fill over the app's
    // hover-restore duration. Those are the only two transitions on the
    // screen: a card that animates its height reflows the list under the
    // pointer.

    [Fact]
    public void The_reason_slot_cross_fades_its_ink_in_120ms()
    {
        var transition = Assert.Single(Transitions(Style(MergeView, "Border.reason > :is(TextBlock)")));

        Assert.Equal("Foreground", transition.Attribute("Property")?.Value);
        Assert.Equal(
            TimeSpan.FromMilliseconds(120),
            TimeSpan.Parse(transition.Attribute("Duration")!.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_row_restores_its_fill_over_the_hover_restore_duration()
    {
        var transition = Assert.Single(Transitions(Style(MergeView, "Border.row")));

        Assert.Equal("Background", transition.Attribute("Property")?.Value);
        Assert.Equal("{StaticResource HoverRestoreDuration}", transition.Attribute("Duration")?.Value);

        // And the token is the 140ms the design system states.
        var token = Assert.Single(
            Load(Tokens).Descendants(),
            e => e.Attribute(Xaml + "Key")?.Value == "HoverRestoreDuration");

        Assert.Equal(
            TimeSpan.FromMilliseconds(140),
            TimeSpan.Parse(token.Value.Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_screen_has_no_third_transition()
    {
        var transitions = Load(MergeView).Descendants()
            .Where(e => e.Name.LocalName.EndsWith("Transition", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, transitions.Count);
    }

    // ── Flare means unread and nothing else ══════════════════════════════════

    [Fact]
    public void Flare_is_the_unread_dot_and_nothing_else()
    {
        var view = Load(MergeView);

        var dots = view.Descendants(Avalonia + "Border")
            .Where(b => b.Attribute("Background")?.Value == "{StaticResource Flare}")
            .ToList();

        Assert.Equal(2, dots.Count);

        foreach (var dot in dots)
        {
            Assert.Equal("{Binding HasUnread}", dot.Attribute("IsVisible")?.Value);
            Assert.Equal("{Binding UnreadTip}", dot.Attribute("ToolTip.Tip")?.Value);
        }

        // No stroke, no ink, no style setter reaches for it.
        var uses = view.Descendants()
            .SelectMany(e => e.Attributes())
            .Count(a => a.Value.Contains("Flare", StringComparison.Ordinal));

        Assert.Equal(2, uses);
    }

    // ── The rail row is a screen, not a cut ══════════════════════════════════

    // FEED and MERGES are screens; the buckets are cuts. A cut carries a count
    // and recedes when it is empty. A screen does neither: the pending count
    // is on the screen's own header.

    [Fact]
    public void The_rail_row_binds_its_label_and_carries_no_count_and_no_opacity()
    {
        var window = Load(Window);

        var label = Assert.Single(
            window.Descendants(Avalonia + "TextBlock"),
            t => t.Attribute("Text")?.Value == "{Binding MergeQueue.RailLabel}");

        var row = label.Ancestors(Avalonia + "Button").First();

        Assert.Equal("{Binding ToggleMergeQueueCommand}", row.Attribute("Command")?.Value);
        Assert.Equal("{Binding MergeQueue.RailTooltip}", row.Attribute("ToolTip.Tip")?.Value);
        Assert.Null(row.Attribute("Opacity"));

        Assert.Single(row.Descendants(Avalonia + "TextBlock"));

        foreach (var attribute in row.DescendantsAndSelf().SelectMany(e => e.Attributes()))
        {
            Assert.DoesNotContain("Count", attribute.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("Opacity", attribute.Value, StringComparison.Ordinal);
        }

        // The old rail's three bindings are gone from the whole window.
        foreach (var gone in new[]
        {
            "MergeQueue.OutstandingCountText", "MergeQueue.HasOutstanding", "MergeQueue.RowOpacity",
        })
        {
            Assert.DoesNotContain(
                window.Descendants().SelectMany(e => e.Attributes()),
                a => a.Value.Contains(gone, StringComparison.Ordinal));
        }
    }

    // ── The dock reads the queue ═════════════════════════════════════════════

    [Fact]
    public void The_dock_binds_the_queue_and_its_one_undo()
    {
        var dock = Dock();

        Assert.Equal("{Binding MergeQueue}", dock.Attribute("DataContext")?.Value);
        Assert.Equal("vm:MergeQueueViewModel", dock.Attribute(Xaml + "DataType")?.Value);
        Assert.Contains("dock", dock.Attribute("Classes")!.Value.Split(' '), StringComparer.Ordinal);

        foreach (var text in new[] { "{Binding DockTitle}", "{Binding DockNote}" })
        {
            Assert.Single(dock.Descendants(Avalonia + "TextBlock"), t => t.Attribute("Text")?.Value == text);
        }

        var undo = Assert.Single(
            dock.Descendants(Avalonia + "Button"),
            b => b.Attribute("Command")?.Value == "{Binding UndoCommand}");
        Assert.Equal("{Binding UndoButtonText}", undo.Attribute("Content")?.Value);
        Assert.Equal("{Binding UndoTooltip}", undo.Attribute("ToolTip.Tip")?.Value);

        var close = Assert.Single(
            dock.Descendants(Avalonia + "Button"),
            b => b.Attribute("Command")?.Value == "{Binding DismissDockCommand}");
        Assert.Equal("{Binding DockCloseTip}", close.Attribute("ToolTip.Tip")?.Value);
    }

    // ── Seven keys, and nothing else ═════════════════════════════════════════

    // Escape leaves. Up and Down walk the rows. Space promotes the focused
    // row. S and Enter answer Same game on the focused card, D answers
    // Different games. Every other key belongs to whatever control has
    // focus, so a letter typed anywhere else on the screen cannot write a
    // link.

    [Fact]
    public void The_merge_queue_answers_seven_keys_and_nothing_else()
    {
        var body = MergeQueueKeyHandler();

        var keys = Regex.Matches(body, @"\bKey\.(\w+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(["Escape", "Up", "Down", "Space", "S", "Enter", "D"], StringComparer.Ordinal),
            keys);

        Assert.DoesNotContain("default:", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_key_does_the_one_thing_the_screen_says_it_does()
    {
        var body = MergeQueueKeyHandler();

        foreach (var (label, call) in new[]
        {
            ("case Key.Escape:", "ShowLibraryCommand.Execute("),
            ("case Key.Up:", "MoveFocus(-1)"),
            ("case Key.Down:", "MoveFocus(1)"),
            ("case Key.Space:", "PromoteFocused()"),
            ("case Key.S or Key.Enter:", "SameGameCommand.Execute(queue.FocusedCard)"),
            ("case Key.D:", "DifferentGamesCommand.Execute(queue.FocusedCard)"),
        })
        {
            var arm = Arm(body, label);

            Assert.Contains(call, arm, StringComparison.Ordinal);
            Assert.Contains("e.Handled = true;", arm, StringComparison.Ordinal);

            // One act per key: nothing else executes in the arm.
            Assert.Single(Regex.Matches(arm, @"\.Execute\(|MoveFocus\(|PromoteFocused\("));
        }
    }

    // ── The shared composites live in one place ══════════════════════════════

    // Button.ctl and the sort menu were the library's; Border.chip and its ✕
    // were the action bar's. The Merges screen is their second wearer, so
    // each has one definition in controls.axaml, written with DynamicResource
    // because that sheet is loaded before the token dictionary is guaranteed
    // to exist.

    [Fact]
    public void The_toolbar_and_chip_styles_are_defined_once_in_controls()
    {
        var styles = Load(Controls).Descendants(Avalonia + "Style")
            .Where(s => SharedSelector.IsMatch(s.Attribute("Selector")?.Value ?? string.Empty))
            .ToList();

        foreach (var selector in new[]
        {
            "Button.ctl", "FlyoutPresenter.sortmenu", "Button.sortitem", "Border.chip", "Button.chipx",
        })
        {
            Assert.Contains(styles, s => s.Attribute("Selector")?.Value == selector);
        }

        foreach (var style in styles)
        {
            foreach (var attribute in style.Descendants().SelectMany(e => e.Attributes()))
            {
                Assert.False(
                    attribute.Value.Contains("StaticResource", StringComparison.Ordinal),
                    $"'{style.Attribute("Selector")!.Value}' reaches a token with StaticResource, "
                    + "which controls.axaml cannot rely on at parse time. Use DynamicResource.");
            }
        }
    }

    [Fact]
    public void No_view_defines_the_shared_styles_again()
    {
        foreach (var view in new[] { Window, "src/Winnow.App/Views/ActionBarView.axaml", MergeView })
        {
            var again = Load(view).Descendants(Avalonia + "Style")
                .Select(s => s.Attribute("Selector")?.Value)
                .Where(s => s is not null && SharedSelector.IsMatch(s))
                .ToList();

            Assert.True(
                again.Count == 0,
                $"{view} defines a style controls.axaml already owns: {string.Join(", ", again)}");
        }
    }

    // ── The deleted surface is unreferenced ══════════════════════════════════

    // The segment strip, the history list and the group cards are gone. A
    // member nothing binds and nothing calls is indistinguishable from one
    // that does not exist, and this screen has carried several of them.

    [Fact]
    public void Every_member_the_rewrite_deleted_is_gone()
    {
        var app = typeof(MergeQueueViewModel).Assembly;

        foreach (var type in new[]
        {
            "MergeGroupViewModel", "ExpansionGroupViewModel", "MergeGroupMemberViewModel",
            "ExpansionMemberViewModel", "MergeLinkHistoryRowViewModel", "MergeSignalViewModel",
            "MergeHistoryLabels",
        })
        {
            Assert.Null(app.GetType("Winnow.App.ViewModels." + type));
        }

        foreach (var (type, member) in new (Type, string)[]
        {
            (typeof(MergeQueueViewModel), "IsReviewVisible"),
            (typeof(MergeQueueViewModel), "IsHistoryVisible"),
            (typeof(MergeQueueViewModel), "IsExpansionsVisible"),
            (typeof(MergeQueueViewModel), "ExpansionGroups"),
            (typeof(MergeQueueViewModel), "LinkHistory"),
            (typeof(MergeQueueViewModel), "SelectedExpansionGroup"),
            (typeof(MergeQueueViewModel), "OutstandingCountText"),
            (typeof(MergeQueueViewModel), "RowOpacity"),
            (typeof(MergeCopy), "ScreenLabel"),
            (typeof(MergeCopy), "SegmentReview"),
            (typeof(ExpansionCopy), "SegmentExpansions"),
            (typeof(ExpansionCopy), "Intro"),
        })
        {
            Assert.Null(type.GetMember(member).FirstOrDefault());
        }

        foreach (var gone in new[] { "IsReviewVisible", "IsHistoryVisible", "ExpansionGroups", "LinkHistory" })
        {
            Assert.DoesNotContain(
                Load(MergeView).Descendants().SelectMany(e => e.Attributes()),
                a => a.Value.Contains(gone, StringComparison.Ordinal));
        }
    }

    // ── Loading ══════════════════════════════════════════════════════════════

    /// <summary>The row template's outer Border.</summary>
    private static XElement RowBorder()
    {
        var template = Assert.Single(
            Load(MergeView).Descendants(Avalonia + "DataTemplate"),
            t => t.Attribute("DataType")?.Value == "vm:MergeRowViewModel");

        var row = Assert.Single(template.Elements(Avalonia + "Border"));
        Assert.Contains("row", row.Attribute("Classes")!.Value.Split(' '), StringComparer.Ordinal);
        return row;
    }

    /// <summary>The merges dock in the shell.</summary>
    private static XElement Dock() => Assert.Single(
        Load(Window).Descendants(Avalonia + "Border"),
        b => b.Attribute("IsVisible")?.Value == "{Binding IsDockOpen}");

    /// <summary>The one style in <paramref name="relativePath"/> with this selector.</summary>
    private static XElement Style(string relativePath, string selector) => Assert.Single(
        Load(relativePath).Descendants(Avalonia + "Style"),
        s => s.Attribute("Selector")?.Value == selector);

    /// <summary>The transitions a style's Transitions setter declares.</summary>
    private static List<XElement> Transitions(XElement style)
    {
        var setter = Assert.Single(
            style.Elements(Avalonia + "Setter"),
            s => s.Attribute("Property")?.Value == "Transitions");

        return Assert.Single(setter.Elements(Avalonia + "Transitions")).Elements().ToList();
    }

    /// <summary>Every public string constant on a copy class.</summary>
    private static HashSet<string> CopyStrings(Type copy) => copy
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => f.GetValue(null))
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>The body of OnMergeQueueKeyDown, as source text.</summary>
    private static string MergeQueueKeyHandler()
    {
        var source = File.ReadAllText(Path(WindowCode));

        var start = source.IndexOf("private void OnMergeQueueKeyDown(", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.axaml.cs has no OnMergeQueueKeyDown.");

        // The method's closing brace is the first one back at the class's
        // indentation; every brace inside the method sits deeper.
        var end = Regex.Match(source[start..], @"\r?\n    \}");
        Assert.True(end.Success, "OnMergeQueueKeyDown does not close where a method closes.");

        return source[start..(start + end.Index + end.Length)];
    }

    /// <summary>One switch arm: from its label to its break.</summary>
    private static string Arm(string body, string label)
    {
        var start = body.IndexOf(label, StringComparison.Ordinal);
        Assert.True(start >= 0, $"OnMergeQueueKeyDown has no '{label}' arm.");

        var end = body.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(end > start, $"The '{label}' arm does not break.");

        return body[start..end];
    }

    private static string Path(string relativePath)
    {
        var root = typeof(MergesSurfaceTests).Assembly
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
