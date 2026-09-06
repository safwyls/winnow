using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// List membership is stored per release — adding a game to a list is an explicit
/// act on the entry the user picked — and resolved per read through live
/// <c>same_game</c> links. A list contains a game when any release of any work in
/// that game's link group is a member, so the list follows the game the user is
/// looking at rather than the one store row they added.
///
/// <para>Membership survives a link because the link model never deletes or
/// repoints a <c>list_items</c> row, and survives re-ingest because
/// <c>OwnershipRepository.UpsertAsync</c> upserts and never deletes. An expansion
/// link (<c>expansion_of</c>) does not carry membership: expansions have their
/// own identity.</para>
/// </summary>
public sealed class ListMembershipResolutionTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly GameListRepository _lists;
    private readonly IdentityLinkRepository _links;

    public ListMembershipResolutionTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _lists = new GameListRepository(_db.Factory);
        _links = new IdentityLinkRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Membership_answers_for_the_release_that_was_added()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        var (workId, releaseId, _) = await SeedGameAsync("Outer Wilds");
        await _lists.AppendItemAsync(listId, releaseId);

        var membership = Assert.Single(await _lists.GetMembershipForGameAsync(workId));

        Assert.Equal(listId, membership.ListId);
        Assert.Equal("Play next", membership.Name);
        Assert.Equal(releaseId, membership.ReleaseId);
    }

    [Fact]
    public async Task A_game_in_no_list_reports_no_membership()
    {
        await _lists.InsertAsync(GameList.Manual("Play next"));
        var (workId, _, _) = await SeedGameAsync("Outer Wilds");

        Assert.Empty(await _lists.GetMembershipForGameAsync(workId));
    }

    [Fact]
    public async Task Membership_survives_linking_two_entries_and_answers_from_both_sides()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        var (steamWorkId, steamReleaseId, _) = await SeedGameAsync("Civilization IV", store: "steam");
        var (gogWorkId, _, _) = await SeedGameAsync("Civilization IV", store: "gog");

        await _lists.AppendItemAsync(listId, steamReleaseId);

        await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = gogWorkId,
            ChildWorkIds = [steamWorkId],
        });

        Assert.Equal(listId, Assert.Single(await _lists.GetMembershipForGameAsync(gogWorkId)).ListId);
        Assert.Equal(listId, Assert.Single(await _lists.GetMembershipForGameAsync(steamWorkId)).ListId);
    }

    [Fact]
    public async Task Retracting_the_link_leaves_the_membership_on_the_release_it_was_added_to()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        var (steamWorkId, steamReleaseId, _) = await SeedGameAsync("Civilization IV", store: "steam");
        var (gogWorkId, _, _) = await SeedGameAsync("Civilization IV", store: "gog");

        await _lists.AppendItemAsync(listId, steamReleaseId);
        var actId = await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = gogWorkId,
            ChildWorkIds = [steamWorkId],
        });

        await _links.RetractActAsync(actId);

        Assert.Single(await _lists.GetMembershipForGameAsync(steamWorkId));
        Assert.Empty(await _lists.GetMembershipForGameAsync(gogWorkId));
    }

    [Fact]
    public async Task An_expansion_link_does_not_carry_membership()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        var (baseWorkId, baseReleaseId, _) = await SeedGameAsync("Civilization IV");
        var (expansionWorkId, _, _) = await SeedGameAsync("Civilization IV: Warlords");

        await _lists.AppendItemAsync(listId, baseReleaseId);
        await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = baseWorkId,
            ChildWorkIds = [expansionWorkId],
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        Assert.Single(await _lists.GetMembershipForGameAsync(baseWorkId));
        Assert.Empty(await _lists.GetMembershipForGameAsync(expansionWorkId));
    }

    [Fact]
    public async Task Member_work_ids_fold_a_linked_pair_into_one_game()
    {
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        var (steamWorkId, steamReleaseId, _) = await SeedGameAsync("Civilization IV", store: "steam");
        var (gogWorkId, gogReleaseId, _) = await SeedGameAsync("Civilization IV", store: "gog");
        var (otherWorkId, otherReleaseId, _) = await SeedGameAsync("Outer Wilds");

        await _lists.AppendItemAsync(listId, steamReleaseId);
        await _lists.AppendItemAsync(listId, gogReleaseId);
        await _lists.AppendItemAsync(listId, otherReleaseId);

        await _links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = gogWorkId,
            ChildWorkIds = [steamWorkId],
        });

        Assert.Equal([gogWorkId, otherWorkId], await _lists.GetMemberWorkIdsAsync(listId));
    }

    [Fact]
    public async Task Membership_survives_a_second_ingest_of_the_same_ownership()
    {
        var resolver = new ExternalIdResolver(
            _works, _releases, _ownerships,
            new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory),
            _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

        var observed = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await resolver.ResolveAsync([Candidate("440", "Team Fortress 2", observed)], CancellationToken.None);

        var release = await _releases.FindByExternalIdAsync(ExternalIdProviders.Steam, "440");
        Assert.NotNull(release);
        var releaseId = release.Id;
        var workId = (await _works.GetAllAsync()).Single().Id;
        var listId = await _lists.InsertAsync(GameList.Manual("Play next"));
        await _lists.AppendItemAsync(listId, releaseId);

        await resolver.ResolveAsync(
            [Candidate("440", "Team Fortress 2", observed.AddDays(1))], CancellationToken.None);

        Assert.Equal(listId, Assert.Single(await _lists.GetMembershipForGameAsync(workId)).ListId);
        Assert.Equal([releaseId], (await _lists.GetItemsAsync(listId)).Select(i => i.ReleaseId));
    }

    private static CandidateOwnership Candidate(string appId, string title, DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: null,
            Installed: false,
            PlaytimeMinutes: 0,
            LastPlayedAt: null,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt);

    private async Task<(long WorkId, long ReleaseId, long OwnershipId)> SeedGameAsync(
        string name, string store = "steam")
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
        });

        return (workId, releaseId, ownershipId);
    }
}
