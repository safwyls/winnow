using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Identity;
using Winnow.Application.Identity;
using Winnow.Application.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Application.Tests;

public sealed class IdentityReviewApplicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-identity-api-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly IdentityReviewApplication _review;
    private readonly ILibraryApplication _library;

    public IdentityReviewApplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationChangePublisher, Changes>();
        services.AddWinnowApplication(Path.Combine(_directory, "winnow.db"), pooling: false);
        services.AddSingleton<IMergeCandidateRepository, Winnow.Data.Repositories.MergeCandidateRepository>();
        services.AddSingleton<IReleaseRepository, Winnow.Data.Repositories.ReleaseRepository>();
        services.AddSingleton<IExpansionRefusalRepository, Winnow.Data.Repositories.ExpansionRefusalRepository>();
        services.AddSingleton<IResolveStateRepository, Winnow.Data.Repositories.ResolveStateRepository>();
        services.AddSingleton<LibraryExpansionScan>();
        services.AddSingleton<IdentityReviewApplication>();
        _services = services.BuildServiceProvider();
        _services.InitializeWinnowDatabase();
        _review = _services.GetRequiredService<IdentityReviewApplication>();
        _library = _services.GetRequiredService<ILibraryApplication>();
    }

    [Fact]
    public async Task LoadingReviewNeverMergesAndExplicitLinkCanBeUndone()
    {
        var parent = await _library.CreateManualGameAsync(new("Same title"));
        var child = await _library.CreateManualGameAsync(new("Same title"));
        var initial = await _review.GetAsync();
        Assert.Equal(2, (await _library.GetLibraryAsync()).Games.Count);
        var linked = await _review.LinkAsync(new(initial.Revision, parent.WorkId, [child.WorkId], IdentityLinkKinds.SameGame, null, [], []));
        Assert.Single((await _library.GetLibraryAsync()).Games);
        await _review.UndoAsync(new(linked.Revision, [linked.ActId!.Value], [], []));
        Assert.Equal(2, (await _library.GetLibraryAsync()).Games.Count);
    }

    [Fact]
    public async Task AnotherClientDecisionRejectsStaleReviewWithoutChangingItsTarget()
    {
        var parent = await _library.CreateManualGameAsync(new("Parent"));
        var first = await _library.CreateManualGameAsync(new("First"));
        var second = await _library.CreateManualGameAsync(new("Second"));
        var initial = await _review.GetAsync();
        await _review.LinkAsync(new(initial.Revision, parent.WorkId, [first.WorkId], IdentityLinkKinds.SameGame, null, [], []));
        await Assert.ThrowsAsync<ApplicationConflictException>(() => _review.LinkAsync(
            new(initial.Revision, parent.WorkId, [second.WorkId], IdentityLinkKinds.SameGame, null, [], [])));
        var resolution = await _services.GetRequiredService<IIdentityLinkRepository>().GetResolutionAsync();
        Assert.Equal(second.WorkId, resolution.SameGame.Resolve(second.WorkId));
    }

    [Fact]
    public async Task DismissAndUndoRestoreCandidateStatusAndRefusalsTogether()
    {
        var parent = await _library.CreateManualGameAsync(new("Parent"));
        var child = await _library.CreateManualGameAsync(new("Parent expansion"));
        var candidates = _services.GetRequiredService<IMergeCandidateRepository>();
        var candidate = await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = parent.ReleaseId, RightReleaseId = child.ReleaseId,
            Score = .9, Status = MergeCandidateStatuses.Pending
        });
        var initial = await _review.GetAsync();
        var pair = new ExpansionRefusalRequest(parent.WorkId, child.WorkId);
        var dismissed = await _review.DismissAsync(new(initial.Revision, [candidate], [pair]));
        Assert.Equal(MergeCandidateStatuses.Rejected, (await candidates.GetAsync(candidate))!.Status);
        Assert.Single(await _services.GetRequiredService<IExpansionRefusalRepository>().GetAllAsync());
        await _review.UndoAsync(new(dismissed.Revision, [], [candidate], [pair]));
        Assert.Equal(MergeCandidateStatuses.Pending, (await candidates.GetAsync(candidate))!.Status);
        Assert.Empty(await _services.GetRequiredService<IExpansionRefusalRepository>().GetAllAsync());
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class Changes : IApplicationChangePublisher
    {
        public void Publish(string kind, string? resource = null) { }
    }
}
