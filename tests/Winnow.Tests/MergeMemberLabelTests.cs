using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The screen-reader labels <see cref="MergeMemberLabels"/> builds for the
/// rows of one card. Kept apart from the Merges screen's source guards
/// because this is arithmetic over facts, not a reading of markup.
/// </summary>
public sealed class MergeMemberLabelTests
{
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

    private static MergeSideViewModel Face(
        string title, int? year, string? publisher, string? store)
        => new(1, title, year, publisher, null, null, store is null ? null : [store]);
}
