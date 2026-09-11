using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class GamesDbIdentityAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removed_or_reassigned_store_identifier_preserves_pending_pair(bool reassign)
    {
        using var db = new TempDatabase();
        var links = new IdentityLinkRepository(db.Factory);
        var left = await SeedAsync(db, "Left");
        var right = await SeedAsync(db, "Right");
        var third = await SeedAsync(db, "Third");
        var releases = new ReleaseRepository(db.Factory);
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = right.Id, Provider = ExternalIdProviders.Steam, ProviderId = "620",
        });
        var request = Request(left, right) with
        {
            ExpectedReleaseIdentities =
            [
                new(left.Id, left.WorkId, 123),
                new(right.Id, right.WorkId, 123, ExternalIdProviders.Steam, "620"),
            ],
        };
        var candidates = new MergeCandidateRepository(db.Factory);
        var candidate = await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left.Id, RightReleaseId = right.Id, Score = .9,
        });
        using (var connection = db.Factory.Open())
        {
            await connection.ExecuteAsync(reassign
                ? "UPDATE external_ids SET release_id = @Id WHERE provider = 'steam' AND provider_id = '620';"
                : "DELETE FROM external_ids WHERE provider = 'steam' AND provider_id = '620';", new { third.Id });
        }

        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => links.LinkAsync(request));

        Assert.Empty(await links.GetHistoryAsync());
        Assert.Empty(await links.GetActsAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, (await candidates.GetAsync(candidate))!.Status);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("work")]
    [InlineData("deleted")]
    public async Task Changed_release_evidence_refuses_without_history_or_queue_changes(string change)
    {
        using var db = new TempDatabase();
        var links = new IdentityLinkRepository(db.Factory);
        var left = await SeedAsync(db, "Left");
        var right = await SeedAsync(db, "Right");
        var third = await SeedAsync(db, "Third");
        var request = Request(left, right);
        using (var connection = db.Factory.Open())
        {
            await connection.ExecuteAsync(change switch
            {
                "version" => "UPDATE releases SET igdb_version_id = 456 WHERE id = @Id;",
                "work" => "UPDATE releases SET work_id = @WorkId WHERE id = @Id;",
                _ => "DELETE FROM releases WHERE id = @Id;",
            }, new { right.Id, third.WorkId });
        }

        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => links.LinkAsync(request));

        Assert.Empty(await links.GetHistoryAsync());
        Assert.Empty(await links.GetActsAsync());
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("rejection")]
    [InlineData("separation")]
    public async Task Decision_on_existing_group_member_blocks_automatic_group_join(string decision)
    {
        using var db = new TempDatabase();
        var links = new IdentityLinkRepository(db.Factory);
        var left = await SeedAsync(db, "Left");
        var right = await SeedAsync(db, "Right");
        var member = await SeedAsync(db, "Existing member");
        var candidates = new MergeCandidateRepository(db.Factory);
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = left.WorkId, ChildWorkIds = [member.WorkId] });
        var request = Request(left, right);
        if (decision == "pin")
        {
            await new WorkIgdbPinRepository(db.Factory).PinAsync(new WorkIgdbPinAssignment { WorkId = member.WorkId, IgdbId = 987 });
        }
        else if (decision == "rejection")
        {
            var candidate = await candidates.InsertAsync(new MergeCandidate
            {
                LeftReleaseId = member.Id, RightReleaseId = right.Id, Score = .9,
            });
            await candidates.SetStatusAsync(candidate, MergeCandidateStatuses.Rejected);
        }
        else
        {
            var former = await SeedAsync(db, "Separated member");
            await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = former.WorkId, ChildWorkIds = [member.WorkId] });
            await links.RetractLinkAsync(member.WorkId);
            await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = left.WorkId, ChildWorkIds = [member.WorkId] });
        }
        var history = await links.GetHistoryAsync();
        var acts = await links.GetActsAsync();

        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => links.LinkAsync(request));

        Assert.Equal(history, await links.GetHistoryAsync());
        Assert.Equal(acts, await links.GetActsAsync());
    }

    [Theory]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Automatic_join_preserves_displaced_relationship_kind_and_safe_undo(string kind)
    {
        using var db = new TempDatabase();
        var links = new IdentityLinkRepository(db.Factory);
        var parent = await SeedAsync(db, "Parent");
        var child = await SeedAsync(db, "Child");
        var expansion = await SeedAsync(db, "Expansion");
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = child.WorkId, ChildWorkIds = [expansion.WorkId], Kind = kind,
        });

        var act = await links.LinkAsync(Request(parent, child));

        var moved = Assert.Single(await links.GetHistoryAsync(), l => l.IsLive && l.ChildWorkId == expansion.WorkId);
        Assert.Equal(kind, moved.Kind);
        Assert.Equal(parent.WorkId, moved.ParentWorkId);
        Assert.Equal(expansion.WorkId, (await links.GetResolutionAsync()).SameGame.Resolve(expansion.WorkId));
        Assert.True(await links.RetractActAsync(act));
        var restored = Assert.Single(await links.GetHistoryAsync(), l => l.IsLive);
        Assert.Equal(child.WorkId, restored.ParentWorkId);
        Assert.Equal(kind, restored.Kind);
    }

    [Fact]
    public async Task Queue_cleanup_failure_rolls_back_identity_act_and_links()
    {
        using var db = new TempDatabase();
        var links = new IdentityLinkRepository(db.Factory);
        var left = await SeedAsync(db, "Left");
        var right = await SeedAsync(db, "Right");
        var candidates = new MergeCandidateRepository(db.Factory);
        await candidates.InsertAsync(new MergeCandidate { LeftReleaseId = left.Id, RightReleaseId = right.Id, Score = .9 });
        using (var connection = db.Factory.Open())
        {
            await connection.ExecuteAsync("""
                CREATE TRIGGER fail_queue_cleanup BEFORE DELETE ON merge_candidates
                BEGIN SELECT RAISE(ABORT, 'test failure'); END;
                """);
        }

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => links.LinkAsync(Request(left, right)));

        Assert.Empty(await links.GetHistoryAsync());
        Assert.Empty(await links.GetActsAsync());
        Assert.Equal(1, await candidates.CountPendingAsync());
    }

    private static IdentityLinkRequest Request(Release parent, Release child) => new()
    {
        ParentWorkId = parent.WorkId, ChildWorkIds = [child.WorkId], Source = IdentityLinkSources.HardId,
        ExpectedSameGameRoots = new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [child.WorkId] = child.WorkId },
        ExpectedReleaseIdentities = [new(parent.Id, parent.WorkId, 123), new(child.Id, child.WorkId, 123)],
    };

    private static async Task<Release> SeedAsync(TempDatabase db, string name)
    {
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = name });
        var release = new Release { WorkId = work, Name = name, IgdbVersionId = 123 };
        var id = await new ReleaseRepository(db.Factory).InsertAsync(release);
        return release with { Id = id };
    }
}
