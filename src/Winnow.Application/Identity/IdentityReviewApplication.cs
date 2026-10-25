using System.Security.Cryptography;
using System.Text.Json;
using Winnow.Api.Contracts.Identity;
using Winnow.Application.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;
using Winnow.Resolve;

namespace Winnow.Application.Identity;

/// <summary>Explicit review decisions; confidence and scan results never authorize a merge.</summary>
public sealed class IdentityReviewApplication(IMergeCandidateRepository candidates, IIdentityLinkRepository links,
    IExpansionRefusalRepository refusals, IGroupHeaderPreferenceRepository headers,
    IResolveStateRepository resolveState, LibraryExpansionScan scan, ILibraryApplication library,
    IUnitOfWorkFactory transactions, IApplicationChangePublisher changes)
{
    public async Task<IdentityReviewResponse> GetAsync(CancellationToken ct = default)
    {
        // The workspace owns its read transaction. Detect a concurrent review change across that boundary.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var revision = await RevisionAsync(ct);
            var pending = await candidates.GetPendingAsync(ct);
            var history = await links.GetHistoryAsync(ct: ct);
            var acts = await links.GetActsAsync(ct);
            var proposals = await scan.ScanAsync(ct);
            var preferences = await library.GetPreferencesAsync(ct);
            var workspace = await library.GetWorkspaceAsync(preferences with { ShowNonGameEntries = true }, ct);
            var complete = await resolveState.GetLastSoftMatchSweepAsync(ct) is not null;
            if (revision != await RevisionAsync(ct)) continue;
            return new(revision, complete, pending, history, acts,
                proposals.Groups.Select(group => new ExpansionGroupResponse(Map(group.Base),
                    group.Members.Select(member => new ExpansionMemberResponse(Map(member.Work), member.Evidence,
                        member.Kind, member.RelationLabel, member.FromMetadata)).ToArray())).ToArray(), workspace);
        }
        throw new ApplicationConflictException("The identity review changed while loading. Reload it.");
    }

    public async Task<IdentityReviewMutation> LinkAsync(IdentityReviewLinkRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        await CheckAsync(request.ExpectedRevision, ct);
        await ValidateCandidatesAsync(request.RejectedCandidateIds, ct);
        var resolution = (await links.GetResolutionAsync(ct)).SameGame;
        var roots = request.ChildWorkIds.Append(request.ParentWorkId).Distinct().ToDictionary(id => id, resolution.Resolve);
        var id = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = request.ParentWorkId, ChildWorkIds = request.ChildWorkIds,
            Kind = request.Kind, RelationLabel = request.RelationLabel, Source = IdentityLinkSources.User,
            ExpectedSameGameRoots = roots
        }, ct);
        foreach (var candidate in request.RejectedCandidateIds.Distinct())
            await candidates.SetStatusAsync(candidate, MergeCandidateStatuses.Rejected, ct);
        await refusals.RefuseAsync(request.RefusedPairs, ct: ct);
        var revision = await RevisionAsync(ct);
        transaction.Commit();
        changes.Publish("library.changed", "identity");
        return new(revision, id);
    }

    public async Task<IdentityReviewMutation> DismissAsync(IdentityReviewDismissRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        await CheckAsync(request.ExpectedRevision, ct);
        await ValidateCandidatesAsync(request.CandidateIds, ct);
        foreach (var candidate in request.CandidateIds.Distinct())
            await candidates.SetStatusAsync(candidate, MergeCandidateStatuses.Rejected, ct);
        await refusals.RefuseAsync(request.RefusedPairs, ct: ct);
        var revision = await RevisionAsync(ct);
        transaction.Commit();
        changes.Publish("library.changed", "identity");
        return new(revision);
    }

    public async Task<IdentityReviewMutation> UndoAsync(IdentityReviewUndoRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        await CheckAsync(request.ExpectedRevision, ct);
        await ValidateCandidatesAsync(request.CandidateIds, ct);
        foreach (var act in request.ActIds.Distinct()) await links.RetractActAsync(act, ct: ct);
        foreach (var candidate in request.CandidateIds.Distinct())
            await candidates.SetStatusAsync(candidate, MergeCandidateStatuses.Pending, ct);
        await refusals.RetractAsync(request.RefusedPairs, ct);
        var revision = await RevisionAsync(ct);
        transaction.Commit();
        changes.Publish("library.changed", "identity");
        return new(revision);
    }

    public async Task<IdentityReviewMutation> SetHeaderAsync(IdentityReviewHeaderRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        await CheckAsync(request.ExpectedRevision, ct);
        var changed = await headers.SetAsync(request.WorkId, request.Store, ct);
        var revision = await RevisionAsync(ct);
        transaction.Commit();
        if (changed) changes.Publish("library.changed", "identity");
        return new(revision, Changed: changed);
    }

    private async Task ValidateCandidatesAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count > 10000) throw new ArgumentException("Too many candidates.");
        foreach (var id in ids.Distinct())
            if (await candidates.GetAsync(id, ct) is null) throw new ApplicationNotFoundException("The candidate no longer exists.");
    }

    private async Task CheckAsync(string expected, CancellationToken ct)
    {
        if (expected != await RevisionAsync(ct)) throw new ApplicationConflictException("The identity review changed. Reload before answering.");
    }

    private async Task<string> RevisionAsync(CancellationToken ct)
    {
        var state = new
        {
            Candidates = await candidates.GetAllAsync(ct), History = await links.GetHistoryAsync(ct: ct),
            Acts = await links.GetActsAsync(ct), Refusals = await refusals.GetAllAsync(ct),
            Headers = (await headers.GetAllAsync(ct)).OrderBy(pair => pair.Key).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
    }

    private static ExpansionWorkResponse Map(ExpansionCandidateWork work) => new(work.WorkId, work.Title, work.ReleaseIds);
}
