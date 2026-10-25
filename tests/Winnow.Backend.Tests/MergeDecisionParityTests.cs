using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Identity;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Backend.Tests;

public sealed class MergeDecisionParityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Twenty_answers_in_sixty_cards_use_bounded_http_reads_and_preserve_pending_candidates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pairs = new List<((long Work, long Release) Parent, (long Work, long Release) Child)>();
        for (var index = 0; index < 60; index++)
        {
            var parent = await fixture.GameAsync($"Bastion {index}", "steam");
            var child = await fixture.GameAsync($"Bastion {index}", "steam");
            await fixture.PairAsync(parent, child);
            pairs.Add((parent, child));
        }
        var review = await fixture.ReviewAsync();
        var before = review;
        Assert.Equal(60, review.Candidates.Count);
        var writes = new List<double>();
        var reads = new List<double>();
        for (var index = 0; index < 20; index++)
        {
            var timer = Stopwatch.StartNew();
            var answer = await fixture.LinkAsync(new(review.Revision, pairs[index].Parent.Work,
                [pairs[index].Child.Work], IdentityLinkKinds.SameGame, null, [], []));
            writes.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart();
            review = await fixture.ReviewAsync();
            reads.Add(timer.Elapsed.TotalMilliseconds);
            Assert.Equal(answer.Revision, review.Revision);
            Assert.Equal(index + 1, review.History.Count(link => link.RetractedAt is null));
        }
        Assert.Equal(20, review.Acts.Count);
        Assert.Equal(60, review.Candidates.Count);
        Assert.All(await fixture.Candidates.GetAllAsync(), candidate => Assert.Equal(MergeCandidateStatuses.Pending, candidate.Status));
        await fixture.AssertEntitiesUnchangedAsync(before);
        output.WriteLine($"60 cards, 20 serial answers; one POST and one authoritative GET per answer. " +
            $"POST mean {writes.Average():F2} ms, max {writes.Max():F2} ms; " +
            $"GET mean {reads.Average():F2} ms, max {reads.Max():F2} ms; combined max {writes.Zip(reads).Max(pair => pair.First + pair.Second):F2} ms.");
    }
    [Fact]
    public async Task Excluding_one_triangle_member_records_only_crossing_rejections_and_undo_restores_them()
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.GameAsync("Prey", "steam");
        var b = await fixture.GameAsync("Prey", "epic");
        var c = await fixture.GameAsync("Prey", "gog");
        var ab = await fixture.PairAsync(a, b);
        var ac = await fixture.PairAsync(a, c);
        var bc = await fixture.PairAsync(b, c);
        var before = await fixture.ReviewAsync();
        var answer = await fixture.LinkAsync(new(before.Revision, a.Work, [b.Work], IdentityLinkKinds.SameGame, null, [ac, bc], []));
        var live = Assert.Single(await fixture.LiveAsync());
        Assert.Equal((a.Work, b.Work), (live.ParentWorkId, live.ChildWorkId));
        Assert.Equal(answer.ActId, live.ActId);
        Assert.Equal(MergeCandidateStatuses.Pending, (await fixture.Candidates.GetAsync(ab))!.Status);
        Assert.Equal(MergeCandidateStatuses.Rejected, (await fixture.Candidates.GetAsync(ac))!.Status);
        Assert.Equal(MergeCandidateStatuses.Rejected, (await fixture.Candidates.GetAsync(bc))!.Status);
        await fixture.UndoAsync(new(answer.Revision, [answer.ActId!.Value], [ac, bc], []));
        Assert.Empty(await fixture.LiveAsync());
        Assert.All((await fixture.ReviewAsync()).Candidates, candidate => Assert.Equal(MergeCandidateStatuses.Pending, candidate.Status));
        Assert.Equal(3, (await fixture.ReviewAsync()).Candidates.Count);
        await fixture.AssertEntitiesUnchangedAsync(before);
    }

    [Fact]
    public async Task Four_link_undo_cycles_preserve_two_child_direction_and_one_act_then_link_again()
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.GameAsync("Prey", "steam");
        var b = await fixture.GameAsync("Prey", "epic");
        var c = await fixture.GameAsync("Prey", "gog");
        await fixture.PairAsync(a, b);
        await fixture.PairAsync(a, c);
        await fixture.PairAsync(b, c);
        var before = await fixture.ReviewAsync();
        for (var cycle = 0; cycle < 5; cycle++)
        {
            var review = await fixture.ReviewAsync();
            var answer = await fixture.LinkAsync(new(review.Revision, b.Work, [a.Work, c.Work], IdentityLinkKinds.SameGame, null, [], []));
            var live = await fixture.LiveAsync();
            Assert.Equal(2, live.Count);
            Assert.All(live, link => { Assert.Equal(b.Work, link.ParentWorkId); Assert.Equal(answer.ActId, link.ActId); });
            Assert.Equal(new[] { a.Work, c.Work }, live.Select(link => link.ChildWorkId).Order().ToArray());
            if (cycle == 4) break;
            await fixture.UndoAsync(new(answer.Revision, [answer.ActId!.Value], [], []));
            Assert.Empty(await fixture.LiveAsync());
            Assert.Equal(3, (await fixture.ReviewAsync()).Candidates.Count);
        }
        await fixture.AssertEntitiesUnchangedAsync(before);
    }

    [Fact]
    public async Task Dismissed_triangle_stays_rejected_after_real_resolver_pass_and_undo_restores_every_edge()
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.GameAsync("Prey", "steam");
        var b = await fixture.GameAsync("Prey", "epic");
        var c = await fixture.GameAsync("Prey", "gog");
        var ids = new[] { await fixture.PairAsync(a, b), await fixture.PairAsync(a, c), await fixture.PairAsync(b, c) };
        var review = await fixture.ReviewAsync();
        await fixture.Client.SendAsync<IdentityReviewDismissRequest, IdentityReviewMutation>(HttpMethod.Post,
            "identity/review/dismiss", new(review.Revision, ids, []));
        await fixture.SweepAsync();
        var refreshed = await fixture.ReviewAsync();
        Assert.Empty(refreshed.Candidates);
        Assert.Empty(await fixture.LiveAsync());
        Assert.All(await fixture.Candidates.GetAllAsync(), candidate => Assert.Equal(MergeCandidateStatuses.Rejected, candidate.Status));
        await fixture.UndoAsync(new(refreshed.Revision, [], ids, []));
        Assert.Equal(ids, (await fixture.ReviewAsync()).Candidates.Select(candidate => candidate.Id).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_then_undo_retracts_current_strip_whether_the_sweep_retires_the_candidate(bool runSweep)
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.GameAsync("Prey", "steam");
        var b = await fixture.GameAsync("Prey", "epic");
        await fixture.PairAsync(a, b);
        var review = await fixture.ReviewAsync();
        var answer = await fixture.LinkAsync(new(review.Revision, a.Work, [b.Work], IdentityLinkKinds.SameGame, null, [], []));
        if (runSweep) await fixture.SweepAsync();
        var refreshed = await fixture.ReviewAsync();
        Assert.Single(refreshed.History, link => link.RetractedAt is null);
        await fixture.UndoAsync(new(refreshed.Revision, [answer.ActId!.Value], [], []));
        var undone = await fixture.ReviewAsync();
        Assert.DoesNotContain(undone.History, link => link.RetractedAt is null);
        Assert.Equal(runSweep ? 0 : 1, undone.Candidates.Count);
    }

    [Fact]
    public async Task Leaving_one_pack_out_refuses_only_that_direction_and_undo_restores_the_scanned_group()
    {
        await using var fixture = await Fixture.CreateAsync();
        var game = await fixture.GameAsync("Sid Meier's Civilization IV", "steam");
        var included = await fixture.GameAsync("Sid Meier's Civilization IV: Warlords", "steam");
        var excluded = await fixture.GameAsync("Sid Meier's Civilization IV: Beyond the Sword", "gog");
        var review = await fixture.ReviewAsync();
        var group = Assert.Single(review.Expansions);
        Assert.Equal(game.Work, group.Base.WorkId);
        Assert.Equal(new[] { included.Work, excluded.Work }, group.Members.Select(member => member.Work.WorkId).Order().ToArray());
        var pairs = new[] { new ExpansionRefusalRequest(game.Work, excluded.Work) };
        var answer = await fixture.LinkAsync(new(review.Revision, game.Work, [included.Work], IdentityLinkKinds.ExpansionOf, "expansion", [], pairs));
        var link = Assert.Single(await fixture.LiveAsync());
        Assert.Equal(IdentityLinkKinds.ExpansionOf, link.Kind);
        Assert.Equal((game.Work, included.Work), (link.ParentWorkId, link.ChildWorkId));
        var refusal = Assert.Single(await fixture.Services.GetRequiredService<IExpansionRefusalRepository>().GetAllAsync());
        Assert.Equal((game.Work, excluded.Work), (refusal.BaseWorkId, refusal.ChildWorkId));
        Assert.Empty((await fixture.ReviewAsync()).Expansions);
        await fixture.UndoAsync(new(answer.Revision, [answer.ActId!.Value], [], pairs));
        Assert.Empty(await fixture.LiveAsync());
        Assert.Empty(await fixture.Services.GetRequiredService<IExpansionRefusalRepository>().GetAllAsync());
        Assert.Equal(2, Assert.Single((await fixture.ReviewAsync()).Expansions).Members.Count);
    }

    [Fact]
    public async Task Stale_structural_answer_conflicts_without_changing_the_pending_candidate_or_external_act()
    {
        await using var fixture = await Fixture.CreateAsync();
        var a = await fixture.GameAsync("Prey", "steam");
        var b = await fixture.GameAsync("Prey", "epic");
        var game = await fixture.GameAsync("Arma 2", "steam");
        var candidate = await fixture.PairAsync(a, b);
        var stale = await fixture.ReviewAsync();
        await fixture.Services.GetRequiredService<IIdentityLinkRepository>().LinkAsync(new IdentityLinkRequest {
            ParentWorkId = game.Work, ChildWorkIds = [a.Work], Kind = IdentityLinkKinds.ExpansionOf,
        });
        var error = await Assert.ThrowsAsync<BackendApiException>(() => fixture.LinkAsync(new(stale.Revision, a.Work, [b.Work], IdentityLinkKinds.SameGame, null, [], [])));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Single(await fixture.Services.GetRequiredService<IIdentityLinkRepository>().GetActsAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, (await fixture.Candidates.GetAsync(candidate))!.Status);
        var unchanged = Assert.Single(await fixture.LiveAsync());
        Assert.Equal((game.Work, a.Work, IdentityLinkKinds.ExpansionOf), (unchanged.ParentWorkId, unchanged.ChildWorkId, unchanged.Kind));
    }

    [Fact]
    public async Task Demo_scan_and_answer_preserve_variant_kind_and_demo_relation_label()
    {
        await using var fixture = await Fixture.CreateAsync();
        var game = await fixture.GameAsync("Sid Meier's Civilization IV", "steam");
        var demo = await fixture.GameAsync("Sid Meier's Civilization IV: Warlords Demo", "steam");
        var review = await fixture.ReviewAsync();
        var group = Assert.Single(review.Expansions);
        var proposed = Assert.Single(group.Members);
        Assert.Equal(game.Work, group.Base.WorkId);
        Assert.Equal(demo.Work, proposed.Work.WorkId);
        Assert.Equal(IdentityLinkKinds.VariantOf, proposed.Kind);
        Assert.Equal(RelationLabels.Demo, proposed.RelationLabel);
        await fixture.LinkAsync(new(review.Revision, game.Work, [demo.Work], proposed.Kind, proposed.RelationLabel, [], []));
        var link = Assert.Single(await fixture.LiveAsync());
        Assert.Equal((game.Work, demo.Work, IdentityLinkKinds.VariantOf, RelationLabels.Demo),
            (link.ParentWorkId, link.ChildWorkId, link.Kind, link.RelationLabel));
        Assert.Empty((await fixture.ReviewAsync()).Expansions);
    }

    private sealed class Fixture(WebApplication host, string directory) : IAsyncDisposable
    {
        public IServiceProvider Services => host.Services;
        public WinnowApiClient Client { get; } = WinnowApiClient.Attach(directory);
        public IMergeCandidateRepository Candidates => Services.GetRequiredService<IMergeCandidateRepository>();
        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-merge-decision-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var host = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await host.StartAsync();
            return new(host, directory);
        }
        public async Task<(long Work, long Release)> GameAsync(string title, string store)
        {
            var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = title, FirstReleaseYear = 2017, Publisher = "Bethesda Softworks" });
            var release = await Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = title });
            await Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership { ReleaseId = release, Store = store });
            return (work, release);
        }
        public async Task<long> PairAsync((long Work, long Release) a, (long Work, long Release) b)
        {
            var score = new SoftMatcher().Score(new() { ReleaseId = a.Release, Title = "Prey", ReleaseYear = 2017, Publisher = "Bethesda Softworks" },
                new() { ReleaseId = b.Release, Title = "Prey", ReleaseYear = 2017, Publisher = "Bethesda Softworks" });
            Assert.True(score.ShouldQueue);
            return await Candidates.InsertAsync(new MergeCandidate { LeftReleaseId = a.Release, RightReleaseId = b.Release, Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending });
        }
        public Task<IdentityReviewResponse> ReviewAsync() => Client.GetAsync<IdentityReviewResponse>("identity/review/");
        public Task<IdentityReviewMutation> LinkAsync(IdentityReviewLinkRequest request) => Client.SendAsync<IdentityReviewLinkRequest, IdentityReviewMutation>(HttpMethod.Post, "identity/review/link", request);
        public Task<IdentityReviewMutation> UndoAsync(IdentityReviewUndoRequest request) => Client.SendAsync<IdentityReviewUndoRequest, IdentityReviewMutation>(HttpMethod.Post, "identity/review/undo", request);
        public Task<SoftMatchSweepReport> SweepAsync() => Services.GetRequiredService<LibrarySoftMatchSweep>().SweepAsync();
        public async Task<IReadOnlyList<IdentityLink>> LiveAsync() => (await Services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync()).Where(link => link.RetractedAt is null).ToArray();
        public async Task AssertEntitiesUnchangedAsync(IdentityReviewResponse before)
        {
            var after = await ReviewAsync();
            Assert.Equal(before.Workspace.Works, after.Workspace.Works);
            Assert.Equal(before.Workspace.Releases, after.Workspace.Releases);
            Assert.Equal(before.Workspace.Ownerships, after.Workspace.Ownerships);
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            await host.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
