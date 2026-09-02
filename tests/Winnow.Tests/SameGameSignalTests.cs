using System.Reflection;
using System.Xml.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.Core.Identity;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Source guards over <c>MergeQueueView.axaml</c>'s signal styles (there is no
/// headless Avalonia renderer, so these hold what the markup declares, exactly as
/// <see cref="StoreChipLayoutTests"/> does) and arithmetic over
/// <see cref="Colorimetry"/> for §8's contrast floor, plus cover-request geometry
/// for expansion pack chips.
/// </summary>
public sealed class SameGameSignalTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    /// <summary>The card's own field: Border.card paints Surface.</summary>
    private static readonly Color Surface = Color.FromRgb(0x16, 0x28, 0x2A);

    /// <summary>The roster row's field: Border.member paints Well.</summary>
    private static readonly Color Well = Color.FromRgb(0x05, 0x0D, 0x0E);

    private static readonly Color TextDim = Color.FromRgb(0x8F, 0xA5, 0xA0);

    private static readonly Color Text = Color.FromRgb(0xF0, 0xED, 0xE7);

    /// <summary>
    /// §8: "TextDim on Surface measures 5.88:1, and on SurfaceRaised — which is
    /// what a selected list row puts under the store and idle columns — 5.04:1.
    /// Do not dim further." 5.04 is the floor the whole screen is held to.
    /// </summary>
    private const double Floor = 5.04;

    // ── The unfired signal ═══════════════════════════════════════════════════

    // No signal style may set Opacity; on a dark field any alpha below 1 lowers contrast.

    [Fact]
    public void The_unfired_signal_row_is_not_dimmed_by_opacity()
    {
        foreach (var style in SignalStyles())
        {
            var opacity = style
                .Elements(Avalonia + "Setter")
                .FirstOrDefault(e => e.Attribute("Property")?.Value == "Opacity");

            Assert.True(
                opacity is null,
                $"'{style.Attribute("Selector")!.Value}' sets Opacity "
                + $"'{opacity?.Attribute("Value")?.Value}'. Every ink on this screen is a "
                + "token on a dark field, so any opacity below 1 lowers contrast: TextDim at "
                + "0.55 composites to 2.79:1 on Surface and 3.01:1 on Well, against §8's "
                + $"{Floor}:1 floor.");
        }
    }

    // The unfired state is carried by demoting the value cell's ink, not by dimming the label or the reason sentence.

    [Fact]
    public void The_unfired_signal_is_marked_by_demoting_its_value_ink()
    {
        var style = Assert.Single(
            SignalStyles(),
            s => s.Attribute("Selector")!.Value.Contains("unfired", StringComparison.Ordinal));

        var setter = Assert.Single(style.Elements(Avalonia + "Setter"));
        Assert.Equal("Foreground", setter.Attribute("Property")!.Value);
        Assert.Equal("{StaticResource TextDim}", setter.Attribute("Value")!.Value);

        // The value cell alone, not the label and not the reason sentence.
        Assert.Contains(".data", style.Attribute("Selector")!.Value, StringComparison.Ordinal);
    }

    // Both states, on both fields the row is drawn on (Surface and Well), clear the §8 floor, and the two states are still distinguishable inks.

    [Fact]
    public void Both_signal_states_clear_the_section_8_floor_on_both_fields()
    {
        foreach (var (fieldName, field) in new[] { ("Surface", Surface), ("Well", Well) })
        {
            // The label and the reason sentence, at both states.
            Assert.True(
                Colorimetry.Contrast(TextDim, field) >= Floor,
                $"TextDim on {fieldName} is {Colorimetry.Contrast(TextDim, field):0.00}:1.");

            // The value cell: Text when the signal fired, TextDim when it did not.
            Assert.True(
                Colorimetry.Contrast(Text, field) >= Floor,
                $"A fired value on {fieldName} is {Colorimetry.Contrast(Text, field):0.00}:1.");

            // And the two states are actually distinguishable inks.
            Assert.True(
                Colorimetry.Contrast(Text, TextDim) > 1.5,
                "A fired and an unfired value read as the same ink.");
        }
    }

    // Recomputes the number the design pass reported, so the fix is held against the measured defect rather than a remembered figure.

    [Fact]
    public void The_retired_opacity_is_the_thing_that_failed_the_floor()
    {
        // The number the design pass computed, recomputed here so the fix is
        // held against the defect rather than against a remembered figure.
        var dimmedOnSurface = Colorimetry.Contrast(At(TextDim, 0.55, Surface), Surface);
        var dimmedOnWell = Colorimetry.Contrast(At(TextDim, 0.55, Well), Well);

        Assert.True(dimmedOnSurface < 3.0, $"{dimmedOnSurface:0.00}:1");
        Assert.True(dimmedOnWell < 3.1, $"{dimmedOnWell:0.00}:1");
        Assert.True(dimmedOnSurface < Floor);
        Assert.True(dimmedOnWell < Floor);
    }

    // ── The pack chip decodes at chip resolution ═════════════════════════════

    // The base asks for capsule width, the pack asks for chip width.

    [Fact]
    public void A_pack_chip_asks_for_a_chip_sized_cover()
    {
        var covers = new RecordingCoverCache();
        var card = new ExpansionGroupViewModel(
            1,
            Side("Sid Meier's Civilization IV", "8930", covers),
            [
                new ExpansionMemberViewModel(
                    2,
                    Side("Sid Meier's Civilization IV: Warlords", "3920", covers),
                    new ExpansionEvidence("civilization iv", "warlords", true, 1, true)),
            ]);

        // What MergeQueueView asks for at 100% scaling: the base capsule's width.
        card.RequestCovers(MergeQueueViewModel.CoverWidth);

        Assert.Equal(
            MergeQueueViewModel.CoverWidth,
            Assert.Single(covers.Widths, w => w.Id == "8930").Width);

        Assert.Equal(
            MergeGroupMemberViewModel.ChipWidth,
            Assert.Single(covers.Widths, w => w.Id == "3920").Width);
    }

    // Both requests scale with the display, so the ratio holds at any render scaling.

    [Fact]
    public void The_chip_request_scales_with_the_display()
    {
        var covers = new RecordingCoverCache();
        var card = new ExpansionGroupViewModel(
            1,
            Side("Sid Meier's Civilization IV", "8930", covers),
            [
                new ExpansionMemberViewModel(
                    2,
                    Side("Sid Meier's Civilization IV: Warlords", "3920", covers),
                    new ExpansionEvidence("civilization iv", "warlords", true, 1, true)),
            ]);

        card.RequestCovers(MergeQueueViewModel.CoverWidth * 1.5);

        Assert.Equal(300, Assert.Single(covers.Widths, w => w.Id == "8930").Width);
        Assert.Equal(96, Assert.Single(covers.Widths, w => w.Id == "3920").Width);
    }

    // ── Members answer to names built from what the row shows ════════════════

    // The entry numbers the automation names leaned on are database ids, and
    // §10.5 rejected showing those; the stores now do most of the
    // disambiguating job on screen. A label therefore starts at the title and
    // takes on the facts the row already draws — stores, then year, then
    // publisher — one at a time, and only while two members would otherwise
    // answer to one name.

    [Fact]
    public void A_title_that_names_one_member_is_the_whole_label()
    {
        var labels = MergeMemberLabels.For(
        [
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Bastion", 2011, "Supergiant Games", "steam"),
        ]);

        Assert.Equal(["Prey", "Bastion"], labels);
    }

    [Fact]
    public void Two_members_with_one_title_take_their_stores()
    {
        var labels = MergeMemberLabels.For(
        [
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Prey", 2017, "Bethesda Softworks", "epic"),
        ]);

        Assert.Equal(["Prey (Steam)", "Prey (Epic)"], labels);
    }

    // The case the entry numbers used to carry on their own: Prey against Prey,
    // both on Steam. The year is the fact that separates them, and it is
    // already printed on the row.
    [Fact]
    public void Two_members_with_one_title_and_one_store_take_their_years()
    {
        var labels = MergeMemberLabels.For(
        [
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Prey", 2006, "3D Realms", "steam"),
        ]);

        Assert.Equal(["Prey (Steam, 2017)", "Prey (Steam, 2006)"], labels);
    }

    [Fact]
    public void Members_a_storefront_describes_identically_take_a_position()
    {
        var labels = MergeMemberLabels.For(
        [
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
        ]);

        Assert.Equal(3, labels.Distinct(StringComparer.Ordinal).Count());
        Assert.All(labels, l => Assert.Contains("of 3", l, StringComparison.Ordinal));
    }

    // Whatever the depth, no label carries a database id.
    [Fact]
    public void No_label_carries_an_entry_number()
    {
        var labels = MergeMemberLabels.For(
        [
            Face("Prey", 2017, "Bethesda Softworks", "steam"),
            Face("Prey", null, null, null),
        ]);

        Assert.All(labels, l => Assert.DoesNotContain("#", l, StringComparison.Ordinal));
        Assert.Equal(2, labels.Distinct(StringComparer.Ordinal).Count());
    }

    // History's own naming rule, deliberately next to the card ladder it
    // diverges from so a reader meets the divergence rather than discovering
    // it. Do not unify these. A card is one question being answered and can
    // afford four qualifying facts; the history log is a list being scanned,
    // and "Prey (Steam, 2017, Bethesda Softworks, 1 of 3)" is the run-on
    // the log exists to avoid. The history rule: the store, once, and only
    // where it actually separates two lines of one row.

    [Fact]
    public void A_history_row_of_distinct_titles_is_left_alone()
    {
        var labels = MergeHistoryLabels.For(
            ["Arma 2", "Arma 2: Operation Arrowhead", "Arma 2: Operation Arrowhead Beta"],
            ["Steam", "Steam", "Steam"]);

        Assert.Equal(
            ["Arma 2", "Arma 2: Operation Arrowhead", "Arma 2: Operation Arrowhead Beta"],
            labels);
    }

    // The child takes the store, and that alone tells the two lines apart, so
    // the headline keeps the plain game name, which is what the headline is for.
    [Fact]
    public void A_child_that_repeats_the_headline_takes_the_store_and_the_headline_does_not()
    {
        var labels = MergeHistoryLabels.For(
            ["The Stanley Parable", "The Stanley Parable"],
            ["Epic", "Steam"]);

        Assert.Equal(["The Stanley Parable", "The Stanley Parable (Steam)"], labels);
    }

    [Fact]
    public void Several_same_titled_children_each_take_their_own_store()
    {
        var labels = MergeHistoryLabels.For(
            ["Prey", "Prey", "Prey"],
            ["Steam", "Epic", "GOG"]);

        Assert.Equal(["Prey", "Prey (Epic)", "Prey (GOG)"], labels);
    }

    // The headline is qualified only when a child would still render the
    // identical string after the children have had their turn. Here the child
    // has no ownership row, so nothing was added to it.
    [Fact]
    public void The_headline_takes_its_store_when_a_child_still_reads_the_same()
    {
        var labels = MergeHistoryLabels.For(["Prey", "Prey"], ["Steam", ""]);

        Assert.Equal(["Prey (Steam)", "Prey"], labels);
    }

    // Where the card ladder would reach for the year, the publisher and then
    // "1 of 3", history states what it knows and stops. A store that every
    // same-titled member shares is not a discriminator, and the log does not
    // print a qualifier that fails to separate anything.
    [Fact]
    public void One_store_across_one_title_adds_nothing_at_all()
    {
        var labels = MergeHistoryLabels.For(
            ["Prey", "Prey", "Prey"],
            ["Steam", "Steam", "Steam"]);

        Assert.Equal(["Prey", "Prey", "Prey"], labels);
        Assert.All(labels, l => Assert.DoesNotContain("(", l, StringComparison.Ordinal));
    }

    private static MergeSideViewModel Face(
        string title, int? year, string? publisher, string? store)
        => new(1, title, year, publisher, null, null, store is null ? null : [store]);

    // ── Helpers ══════════════════════════════════════════════════════════════

    private static MergeSideViewModel Side(string title, string appId, ICoverCache covers)
        => new(
            releaseId: 1,
            title: title,
            coverKey: CoverKey.Steam(appId),
            covers: covers);

    /// <summary><paramref name="ink"/> at <paramref name="alpha"/> over <paramref name="field"/>.</summary>
    private static Color At(Color ink, double alpha, Color field)
        => Colorimetry.Over(
            Color.FromArgb((byte)Math.Round(alpha * 255), ink.R, ink.G, ink.B), field);

    private static List<XElement> SignalStyles()
    {
        var root = typeof(SameGameSignalTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        Assert.False(
            string.IsNullOrWhiteSpace(root),
            "The test assembly carries no RepositoryRoot metadata, so the markup cannot be read.");

        var path = Path.Combine(
            root!, "src", "Winnow.App", "Views", "MergeQueueView.axaml");
        Assert.True(File.Exists(path), $"The markup was not found at '{path}'.");

        var styles = XDocument.Load(path).Root!
            .Descendants(Avalonia + "Style")
            .Where(s => s.Attribute("Selector")?.Value.Contains(".signal", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(styles);
        return styles;
    }

    /// <summary>Records the display width every cover was asked for. Serves no art.</summary>
    private sealed class RecordingCoverCache : ICoverCache
    {
        public List<(string Id, double Width)> Widths { get; } = [];

        public bool TryGet(CoverKey key, double displayWidthPixels, out CoverArt art)
        {
            Widths.Add((key.Id, displayWidthPixels));
            art = null!;
            return false;
        }

        public Task<CoverArt?> GetAsync(
            CoverKey key, double displayWidthPixels, CancellationToken ct = default)
            => Task.FromResult<CoverArt?>(null);
    }
}
