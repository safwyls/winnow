using System.Text.RegularExpressions;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// The rules the details modal's structure has to keep, read off the markup
/// itself. Each is a rule the modal has already lost once, or one the next
/// addition is most likely to break.
/// </summary>
public sealed class DetailsModalStructureTests
{
    private const string View = "src/Winnow.App/Views/GameDetailsView.axaml";

    /// <summary>
    /// The order §10.1 fixes: corrections, then what shipped, then the prose
    /// and the pictures inside it, then the three grouping sections, then the
    /// lists. Read off the markup, because the order the file declares IS the
    /// order the band draws and the order Tab walks.
    /// </summary>
    [Fact]
    public void The_rest_band_declares_its_sections_in_the_order_the_design_fixes()
    {
        var markup = RepositoryTree.Read(View);

        string[] anchors =
        [
            "{Binding IgdbMatch.OpenLabel}",
            "{Binding MetadataEditor.OpenLabel}",
            "{Binding UpdatesLabel}",
            "{Binding AboutHeading}",
            "IsVisible=\"{Binding ShowCoverage}\"",
            "IsVisible=\"{Binding ShowExtends}\"",
            "IsVisible=\"{Binding ShowExpansions}\"",
            "IsVisible=\"{Binding ShowLists}\"",
        ];

        var positions = anchors
            .Select(a => (Anchor: a, At: markup.IndexOf(a, StringComparison.Ordinal)))
            .ToList();

        Assert.All(positions, p => Assert.True(p.At >= 0, $"{View} no longer contains {p.Anchor}"));

        for (var i = 1; i < positions.Count; i++)
        {
            Assert.True(
                positions[i].At > positions[i - 1].At,
                $"{positions[i].Anchor} is declared before {positions[i - 1].Anchor} in {View}");
        }
    }

    /// <summary>
    /// The update list's heading and Band 2's rail label were the same string,
    /// so one modal said SINCE YOU PLAYED twice about two different things.
    /// The rail keeps the name; the list took one of its own, and it is
    /// constant whether or not anything landed since the last session.
    /// </summary>
    [Fact]
    public void The_update_heading_does_not_collide_with_the_rail_label()
    {
        Assert.NotEqual("SINCE YOU PLAYED", GameDetailsCopy.UpdatesHeading);
        Assert.NotEmpty(GameDetailsCopy.UpdatesHeading);

        var markup = RepositoryTree.Read(View);

        // The rail label is a literal in the markup and appears once per Band 2
        // branch — the axis and the gap rail — and nowhere else.
        Assert.Equal(2, Regex.Matches(markup, "\"SINCE YOU PLAYED\"").Count);
    }

    /// <summary>
    /// §8 says do not dim further, and the figures say the same: <c>TextFaint</c>
    /// is under AA on the flat card in every theme before a cover is involved
    /// at all. The mock this pass was built from styles the reception line's
    /// source attribution in the faint ink; the shipped panel must not.
    /// </summary>
    [Fact]
    public void No_run_in_the_modal_takes_the_ink_below_the_floor()
    {
        // The editor's own :disabled setter is the one legitimate use in the
        // modal's tree and is not this rule: a control that cannot be pressed
        // is meant to read as unavailable, and WCAG exempts it.
        Assert.DoesNotContain("TextFaint", RepositoryTree.Read(View), StringComparison.Ordinal);
    }

    /// <summary>
    /// The refetch status is a live region, and a live region has to be a
    /// TextBlock whose Text is bound: changing an
    /// <c>AutomationProperties.Name</c> at runtime raises no UIA event, while
    /// <c>TextBlockAutomationPeer</c> raises a Name change whenever Text
    /// changes. Verified against the Avalonia 11.3.20 source; pinned here so a
    /// later refactor cannot swap the binding for a name and silently go quiet.
    /// </summary>
    [Fact]
    public void The_refetch_status_is_a_bound_text_live_region()
    {
        var markup = RepositoryTree.Read(View);

        var field = Regex.Match(
            markup,
            @"<TextBlock[^>]*Name=""RefetchStatus""[^>]*>",
            RegexOptions.Singleline);

        Assert.True(field.Success, "The refetch status field is gone from the modal.");
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", field.Value, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Refetch.Status}\"", field.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name", field.Value, StringComparison.Ordinal);

        // Outside the rest band's ScrollViewer: an act started from a popup is
        // answered where it can always be seen.
        var scroll = markup.IndexOf("<ScrollViewer Grid.Row=\"2\"", StringComparison.Ordinal);
        Assert.True(scroll > 0);
        Assert.True(field.Index < scroll, "The refetch status moved inside the rest band's scroll region.");
    }

    /// <summary>
    /// Every popup in this panel is the action band's menu, and the menu is
    /// allowed only because it draws its own focus mark inside the item
    /// template (§10.7). Screenshots and the reception line are surfaces to
    /// look at and stay in the modal's own tree.
    /// </summary>
    [Fact]
    public void The_panel_still_has_exactly_one_popup()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Single(Regex.Matches(markup, "<MenuFlyout"));
        Assert.DoesNotContain("<Flyout", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<Popup", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ContextFlyout", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The menu's rows, in order. Refetch joins them because the strip is
    /// "get me in" and re-asking a source moves nobody closer to playing.
    /// </summary>
    [Fact]
    public void The_menu_carries_five_rows_in_the_settled_order()
    {
        var markup = RepositoryTree.Read(View);

        string[] rows =
        [
            "Name=\"OpenFolderItem\"",
            "Name=\"RefetchItem\"",
            "Name=\"WrongGameItem\"",
            "Name=\"EditDetailsItem\"",
            "Name=\"HideItem\"",
        ];

        var at = rows.Select(r => markup.IndexOf(r, StringComparison.Ordinal)).ToList();
        Assert.All(at, i => Assert.True(i >= 0));

        for (var i = 1; i < at.Count; i++)
        {
            Assert.True(at[i] > at[i - 1], $"{rows[i]} is declared before {rows[i - 1]}");
        }
    }

    /// <summary>
    /// Every bounded scroll region in the modal clears its own bar, because
    /// Fluent draws the bar over the content rather than beside it. The three
    /// vertical regions take the trailing gutter; the screenshot strip, whose
    /// bar is horizontal, takes the same width at its foot.
    /// </summary>
    [Fact]
    public void Every_bounded_scroll_region_clears_its_own_bar()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Equal(3, Regex.Matches(markup, @"\{StaticResource InnerScrollGutter\}").Count);
        Assert.Single(Regex.Matches(markup, @"\{StaticResource InnerScrollGutterBottom\}"));
    }

    /// <summary>
    /// The original fault: absolute pixels on the card.
    /// <c>MinWidth="700"</c> and <c>Margin="40"</c> must stay, both caps
    /// must come from the scaling resources, and no <c>MaxWidth</c> or
    /// <c>MaxHeight</c> on the card may be a literal number again.
    /// </summary>
    [Fact]
    public void The_card_is_capped_against_the_window_and_not_by_a_number_in_the_file()
    {
        var markup = RepositoryTree.Read(View);

        var card = Regex.Match(markup, @"<Border Name=""Card""(.*?)>", RegexOptions.Singleline);
        Assert.True(card.Success, "The modal's card border is gone.");

        Assert.Contains("MinWidth=\"700\"", card.Value, StringComparison.Ordinal);
        Assert.Contains("Margin=\"40\"", card.Value, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource CardWidthCap}", card.Value, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource CardHeightCap}", card.Value, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"Max(Width|Height)=""\d", card.Value);
    }

    /// <summary>The two fractions and their floors are measured values from
    /// <c>docs/spikes/details-modal-scale.md</c>. No hero height cap must
    /// appear: the screenshot opens in the lightbox overlay, which is capped
    /// against the shot's native size rather than against a fraction of the
    /// window.</summary>
    [Fact]
    public void The_scaling_resources_carry_the_measured_numbers()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Contains(
            @"<conv:ScaledLength x:Key=""CardWidthCap"" Fraction=""0.5"" Least=""860"" Most=""1582""/>",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            @"<conv:ScaledLength x:Key=""CardHeightCap"" Fraction=""0.667"" Least=""720""/>",
            markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("HeroHeightCap", markup, StringComparison.Ordinal);
    }

    /// <summary>The thumbnail strip stays in ABOUT as the entry point, and
    /// every thumbnail is a real <c>Button</c> with a Tab stop and a drawn
    /// focus ring. The hero that used to expand above the strip is gone, and
    /// its window-fraction cap with it.</summary>
    [Fact]
    public void The_strip_stays_and_the_hero_is_gone()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Contains("Button Classes=\"shot\"", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding ListAutomationName}", markup, StringComparison.Ordinal);

        Assert.DoesNotContain("HasHero", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroAutomationName", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Hero}", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// ABOUT's two prose runs carry the <c>.prose</c> class, and the
    /// <c>ProseMeasure</c> token still holds the measured 410.
    /// </summary>
    [Fact]
    public void The_prose_runs_take_the_reading_measure()
    {
        var markup = RepositoryTree.Read(View);

        Assert.Contains("Classes=\"body prose copyable\"", markup, StringComparison.Ordinal);
        Assert.Contains("Classes=\"body prose\"", markup, StringComparison.Ordinal);

        var tokens = RepositoryTree.Read("src/Winnow.App/Themes/tokens.axaml");
        Assert.Contains("<x:Double x:Key=\"ProseMeasure\">410</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("{StaticResource ProseMeasure}", tokens, StringComparison.Ordinal);
    }
}
