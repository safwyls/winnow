using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Dapper;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The variant_of kind (migration 0021), which is
/// <see cref="DemoConsolidation"/>'s read-time rule made into a stored fact
/// with a storefront source behind it.
///
/// <para>A variant does not count as a title while its parent is owned, counts
/// when it is the only thing owned, and never rolls up playtime. The last
/// clause is the app's premise rather than an omission: forty minutes of a demo
/// you never bought is exactly the kind of thing Winnow exists to show you, so
/// the hours stay on the variant's own row and reach the parent's modal from
/// there.</para>
/// </summary>
public sealed class VariantLinkTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private int _appId = 900_000;

    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;
    private readonly LibraryQueryRepository _queries;
    private readonly IdentityLinkRepository _links;
    private readonly ExpansionRefusalRepository _refusals;

    public VariantLinkTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _queries = new LibraryQueryRepository(_db.Factory);
        _links = new IdentityLinkRepository(_db.Factory);
        _refusals = new ExpansionRefusalRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The whole rule in one test, at the grain the library counts in.
    ///
    /// <para>The titles are deliberately unrelated so that nothing
    /// <see cref="DemoConsolidation"/> does can account for the result: the only
    /// thing binding these two rows is the stored link.</para>
    /// </summary>
    [Fact]
    public async Task A_variant_stops_counting_as_a_title_only_while_its_parent_is_owned()
    {
        var parent = await SeedAsync("Bastion", minutes: 600);
        var variant = await SeedAsync("Supergiant Sample Build", minutes: 40);

        // Two products, two titles, before anyone says otherwise.
        Assert.Equal(2, await TitleCountAsync());

        var act = await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent.WorkId,
            ChildWorkIds = [variant.WorkId],
            Kind = IdentityLinkKinds.VariantOf,
            RelationLabel = RelationLabels.Demo,
        });

        // One title: the sample is the game, and the game is owned.
        var survivor = Assert.Single(
            await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default));
        Assert.Equal(parent.WorkId, survivor.WorkId);

        // The parent says how many rows it stands in for, so nothing is hidden
        // silently.
        Assert.Equal(1, survivor.ConsolidatedDemoCount);

        // PLAYTIME DOES NOT ROLL UP. 600 minutes stay 600 minutes; the demo's
        // 40 are not added to the game the user actually bought.
        Assert.Equal(600, survivor.PlaytimeMinutes);

        // Take the parent out of the library and the sample is the only copy
        // there is, so it counts again — and nothing was written to make that
        // happen, exactly as demo consolidation has always behaved.
        using (var lease = _db.Factory.Lease())
        {
            lease.Connection.Execute(
                "DELETE FROM ownerships WHERE id = @id;",
                new { id = parent.OwnershipId },
                lease.Transaction);
        }

        var alone = await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        var only = Assert.Single(alone);
        Assert.Equal(variant.WorkId, only.WorkId);
        Assert.Equal(40, only.PlaytimeMinutes);
        Assert.Equal(0, only.ConsolidatedDemoCount);

        // And the link is still standing: it was never the ownership that made
        // it true.
        var live = await _links.GetHistoryAsync(variant.WorkId);
        Assert.True(Assert.Single(live).IsLive);
        Assert.Equal(act, live[0].ActId);
    }

    /// <summary>
    /// The label survives the round trip, which is what lets a card say "Demo"
    /// while only three kinds exist. THE CARD CONTRACT: read
    /// <see cref="IdentityLink.RelationLabel"/>, not the kind.
    /// </summary>
    [Fact]
    public async Task The_relation_label_is_stored_and_read_back()
    {
        var parent = await SeedAsync("Dishonored 2", minutes: 100);
        var child = await SeedAsync("Dishonored: Death of the Outsider", minutes: 30);

        await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent.WorkId,
            ChildWorkIds = [child.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
            RelationLabel = RelationLabels.StandaloneExpansion,
        });

        var link = Assert.Single(await _links.GetHistoryAsync(child.WorkId));
        Assert.Equal(IdentityLinkKinds.ExpansionOf, link.Kind);
        Assert.Equal(RelationLabels.StandaloneExpansion, link.RelationLabel);

        var resolution = await _links.GetResolutionAsync();
        Assert.Equal(parent.WorkId, resolution.Expansions.BaseOf(child.WorkId));
        Assert.True(resolution.Variants.IsEmpty);
    }

    /// <summary>
    /// An expansion is a title and a variant is not, which is the difference
    /// between the two kinds stated as a number rather than as a comment.
    /// </summary>
    [Fact]
    public async Task An_expansion_still_counts_as_a_title_where_a_variant_does_not()
    {
        var baseGame = await SeedAsync("Sid Meier's Civilization IV", minutes: 1800);
        var expansion = await SeedAsync("Sid Meier's Civilization IV: Warlords", minutes: 300);

        await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = baseGame.WorkId,
            ChildWorkIds = [expansion.WorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
            RelationLabel = RelationLabels.Expansion,
        });

        var buckets = await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, buckets.Count);
        Assert.Equal(
            [300L, 1800L],
            buckets.Select(b => b.PlaytimeMinutes).Order());
    }

    /// <summary>
    /// The queue must not offer the rows the library hides.
    /// <see cref="LibraryExpansionScan"/> reads
    /// <see cref="IReleaseRepository.GetIdentitiesAsync"/>, which returns every
    /// release as stored, while demo consolidation runs inside the bucket
    /// query — so eleven of the author's 38 proposals were rows the grid has
    /// never once shown, offered under the word "expansion".
    /// </summary>
    [Fact]
    public async Task The_scan_does_not_offer_a_row_the_library_already_hides()
    {
        await SeedAsync("Bastion", minutes: 600);
        await SeedAsync("Bastion Demo", minutes: 40);

        // The library shows one tile: consolidation has always hidden this row.
        var buckets = await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(600, Assert.Single(buckets).PlaytimeMinutes);

        // And now the queue agrees with it.
        var scan = new LibraryExpansionScan(_releases, _links, _refusals);
        var report = await scan.ScanAsync();
        Assert.Empty(report.Groups);
    }

    /// <summary>
    /// The other half of that: a demo whose full game is NOT owned is a real
    /// entry in the library, so the scan is still free to talk about it. The
    /// suppression is about redundancy, never about demos.
    /// </summary>
    [Fact]
    public async Task An_unaccompanied_demo_is_still_visible_to_both()
    {
        await SeedAsync("Bastion Demo", minutes: 40);

        var buckets = await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(40, Assert.Single(buckets).PlaytimeMinutes);

        var scan = new LibraryExpansionScan(_releases, _links, _refusals);
        var report = await scan.ScanAsync();
        Assert.Equal(1, report.Works);
    }

    private async Task<int> TitleCountAsync()
        => (await _queries.GetOwnershipBucketsAsync(BucketThresholds.Default)).Count;

    private async Task<Seeded> SeedAsync(string title, long minutes, int? year = 2011)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = title,
            FirstReleaseYear = year,
            Publisher = "Supergiant",
        });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = title,
            Platform = "windows",
        });

        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "steam_localconfig",
            ObservedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        return new Seeded(workId, releaseId, ownershipId);
    }

    private sealed record Seeded(long WorkId, long ReleaseId, long OwnershipId);
}
