using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="ExpansionRefusalRepository.RetractAsync"/>: the write behind the
/// dock's Undo after a "Different games". A refusal is the one stored answer
/// on the expansion side, so taking it back has to remove exactly the row
/// that was written and nothing beside it, and has to say how many it took.
/// </summary>
public sealed class ExpansionRefusalRetractTests
{
    // ── Retracting a refused pair removes it ═════════════════════════════════

    [Fact]
    public async Task Retracting_a_refused_pair_removes_it_and_counts_one()
    {
        using var db = new TempDatabase();
        var (civ, bts) = await SeedPairAsync(db);
        var refusals = new ExpansionRefusalRepository(db.Factory);

        await refusals.RefuseAsync([new ExpansionRefusalRequest(civ, bts)]);
        Assert.Single(await refusals.GetAllAsync());

        var removed = await refusals.RetractAsync([new ExpansionRefusalRequest(civ, bts)]);

        Assert.Equal(1, removed);
        Assert.Empty(await refusals.GetAllAsync());
    }

    // ── An unknown pair is left alone, and so is everything else ═════════════

    // Directional, like the refusal itself: retracting the reverse claim is
    // retracting a refusal that was never given, and the one that WAS given
    // must survive it.
    [Fact]
    public async Task Retracting_an_unknown_pair_counts_zero_and_leaves_the_others()
    {
        using var db = new TempDatabase();
        var (civ, bts) = await SeedPairAsync(db);
        var works = new WorkRepository(db.Factory);
        var warlords = await works.InsertAsync(new Work { Name = "Sid Meier's Civilization IV: Warlords" });
        var refusals = new ExpansionRefusalRepository(db.Factory);

        await refusals.RefuseAsync(
        [
            new ExpansionRefusalRequest(civ, bts),
            new ExpansionRefusalRequest(civ, warlords),
        ]);

        var removed = await refusals.RetractAsync(
        [
            new ExpansionRefusalRequest(bts, civ),
            new ExpansionRefusalRequest(warlords, bts),
        ]);

        Assert.Equal(0, removed);

        var standing = await refusals.GetAllAsync();
        Assert.Equal(2, standing.Count);
        Assert.Contains(standing, r => r.BaseWorkId == civ && r.ChildWorkId == bts);
        Assert.Contains(standing, r => r.BaseWorkId == civ && r.ChildWorkId == warlords);
    }

    // A mixed list removes the pair it names and only that one.
    [Fact]
    public async Task Retracting_one_of_two_refusals_removes_only_that_one()
    {
        using var db = new TempDatabase();
        var (civ, bts) = await SeedPairAsync(db);
        var works = new WorkRepository(db.Factory);
        var warlords = await works.InsertAsync(new Work { Name = "Sid Meier's Civilization IV: Warlords" });
        var refusals = new ExpansionRefusalRepository(db.Factory);

        await refusals.RefuseAsync(
        [
            new ExpansionRefusalRequest(civ, bts),
            new ExpansionRefusalRequest(civ, warlords),
        ]);

        var removed = await refusals.RetractAsync(
        [
            new ExpansionRefusalRequest(civ, bts),
            new ExpansionRefusalRequest(civ, 999_999),
        ]);

        Assert.Equal(1, removed);

        var survivor = Assert.Single(await refusals.GetAllAsync());
        Assert.Equal(civ, survivor.BaseWorkId);
        Assert.Equal(warlords, survivor.ChildWorkId);
    }

    // ── An empty list writes nothing ═════════════════════════════════════════

    [Fact]
    public async Task An_empty_list_writes_nothing()
    {
        using var db = new TempDatabase();
        var (civ, bts) = await SeedPairAsync(db);
        var refusals = new ExpansionRefusalRepository(db.Factory);

        await refusals.RefuseAsync([new ExpansionRefusalRequest(civ, bts)]);
        var before = await refusals.GetAllAsync();

        var removed = await refusals.RetractAsync([]);

        Assert.Equal(0, removed);

        var after = await refusals.GetAllAsync();
        Assert.Equal(before.Select(r => r.Id), after.Select(r => r.Id));
        Assert.Equal(before.Select(r => r.RefusedAt), after.Select(r => r.RefusedAt));
    }

    // ── Seeding ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Two works with rows of their own, because migration 0020 cascades a
    /// refusal from <c>works</c> and a pair of made-up ids would be testing
    /// the wrong thing.
    /// </summary>
    private static async Task<(long BaseWorkId, long ChildWorkId)> SeedPairAsync(TempDatabase db)
    {
        var works = new WorkRepository(db.Factory);
        var civ = await works.InsertAsync(new Work { Name = "Sid Meier's Civilization IV" });
        var bts = await works.InsertAsync(new Work { Name = "Sid Meier's Civilization IV: Beyond the Sword" });
        return (civ, bts);
    }
}
