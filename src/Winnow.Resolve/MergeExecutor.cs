using Winnow.Core.Merging;
using Winnow.Core.Repositories;
using Winnow.Resolve.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Resolve;

/// <summary>What one <see cref="MergeExecutor.ApplyAllConfirmedAsync"/> pass did.</summary>
public sealed record MergeExecutionSummary(
    int Considered,
    int Applied,
    int Collapsed,
    int WorkOnly,
    int Skipped)
{
    public static MergeExecutionSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Applies decisions the user already made. It cannot make one: a pair that is
/// not <c>status = 'confirmed'</c> is refused by the repository's own SQL
/// predicate, so section 5.3's "fuzzy matches never auto-merge" holds no matter
/// what is asked here.
///
/// <para>Its one addition to the repository's verdict is a downgrade. When the
/// queued signals payload recorded different bundle editions on the two sides (a
/// "Gold Edition" against the base game), it sets
/// <see cref="MergeRequest.AllowReleaseCollapse"/> to <c>false</c> so the two
/// editions stay two releases under one work. That flag is a ceiling; it can
/// never ask for more than the repository's own safety verdict permits.</para>
///
/// <para>Holds no SQL and no connection. Winnow.Resolve depends on Core
/// abstractions only (section 5.1).</para>
/// </summary>
public sealed class MergeExecutor
{
    private readonly IMergeCandidateRepository _candidates;
    private readonly IMergeExecutionRepository _merges;
    private readonly IMergeUndoRepository? _undo;
    private readonly ILogger<MergeExecutor> _logger;

    // The undo repository is optional so the composition root can register it in
    // the pass that adds the merge history screen without this constructor
    // changing again. The undo wrappers below say so plainly rather than
    // throwing a null reference.
    public MergeExecutor(
        IMergeCandidateRepository candidates,
        IMergeExecutionRepository merges,
        IMergeUndoRepository? undo = null,
        ILogger<MergeExecutor>? logger = null)
    {
        _candidates = candidates;
        _merges = merges;
        _undo = undo;
        _logger = logger ?? NullLogger<MergeExecutor>.Instance;
    }

    /// <summary>
    /// Reads only. Returns the plan the confirm screen shows before the user
    /// commits, including the surviving identity and any blocker.
    /// </summary>
    public async Task<MergePlan> PreviewAsync(long candidateId, CancellationToken ct = default)
        => await _merges.PlanAsync(await RequestAsync(candidateId, ct), ct);

    /// <summary>
    /// Reads only. Every confirmed pair not yet applied, each already planned, in
    /// the order the batch pass would take them. Exists so <c>Winnow.App</c> can
    /// show that list without naming <see cref="IMergeExecutionRepository"/>: the
    /// policy stays here (section 5.1).
    /// </summary>
    public async Task<IReadOnlyList<MergePlan>> OutstandingAsync(CancellationToken ct = default)
    {
        var pending = await _merges.GetConfirmedUnappliedCandidateIdsAsync(ct);
        if (pending.Count == 0)
        {
            return [];
        }

        var plans = new List<MergePlan>(pending.Count);
        foreach (var candidateId in pending)
        {
            ct.ThrowIfCancellationRequested();
            plans.Add(await _merges.PlanAsync(await RequestAsync(candidateId, ct), ct));
        }

        return plans;
    }

    public async Task<MergeOutcome> ApplyAsync(long candidateId, CancellationToken ct = default)
    {
        var outcome = await _merges.ApplyAsync(await RequestAsync(candidateId, ct), ct);
        Log(outcome);
        return outcome;
    }

    /// <summary>
    /// Applies every confirmed pair whose sides do not yet share a work. Each
    /// pair is its own transaction: one pair that cannot merge safely does not
    /// hold back the rest.
    /// </summary>
    public async Task<MergeExecutionSummary> ApplyAllConfirmedAsync(CancellationToken ct = default)
    {
        var pending = await _merges.GetConfirmedUnappliedCandidateIdsAsync(ct);
        if (pending.Count == 0)
        {
            return MergeExecutionSummary.Empty;
        }

        var applied = 0;
        var collapsed = 0;
        var workOnly = 0;
        var skipped = 0;

        foreach (var candidateId in pending)
        {
            ct.ThrowIfCancellationRequested();

            var outcome = await _merges.ApplyAsync(await RequestAsync(candidateId, ct), ct);
            Log(outcome);

            if (!outcome.Applied)
            {
                skipped++;
                continue;
            }

            applied++;
            if (outcome.Plan.Mode == MergeMode.ReleaseCollapse)
            {
                collapsed++;
            }
            else
            {
                workOnly++;
            }
        }

        _logger.LogInformation(
            "Merge execution: {Considered} confirmed pair(s), {Applied} applied "
            + "({Collapsed} collapsed to one release, {WorkOnly} unified at the work only), "
            + "{Skipped} skipped.",
            pending.Count, applied, collapsed, workOnly, skipped);

        return new MergeExecutionSummary(pending.Count, applied, collapsed, workOnly, skipped);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads only. The verdict on whether one applied merge can still be
    /// reversed, and every reason it cannot. The history screen calls this on
    /// every load and never caches the answer: reversibility depends on every
    /// merge applied after this one.
    /// </summary>
    public async Task<MergeUndoPlan> PreviewUndoAsync(long applicationId, CancellationToken ct = default)
        => await Undo.PlanUndoAsync(applicationId, ct);

    /// <summary>
    /// Every applied merge, newest first, each with its reversibility already
    /// computed.
    /// </summary>
    public async Task<IReadOnlyList<MergeUndoPlan>> HistoryAsync(CancellationToken ct = default)
        => await Undo.ListUndoPlansAsync(ct);

    /// <summary>
    /// Atomic. Restores the absorbed identity and everything repointed away from
    /// it, or throws and leaves the database exactly as it was. Never partial.
    /// Marks the <c>merge_applications</c> row undone and sets the pair to
    /// status <c>undone</c>, which is terminal, so no sweep re-queues it and no
    /// batch pass re-applies it; re-merging needs a deliberate re-confirmation.
    /// </summary>
    public async Task<MergeUndoResult> UndoAsync(long applicationId, CancellationToken ct = default)
    {
        var result = await Undo.UndoAsync(applicationId, ct);

        _logger.LogInformation(
            "Undid merge application {ApplicationId}: work {Work} and release {Release} restored, "
            + "{Reinserted} row(s) re-inserted, {Repointed} repointed back, {InPlace} restored in place"
            + "{Reused}.",
            applicationId, result.RestoredWorkId, result.RestoredReleaseId,
            result.RowsReinserted, result.RowsRepointedBack, result.RowsRestoredInPlace,
            result.IdentityIdsReused ? " (at a fresh id, the original having been reused)" : string.Empty);

        return result;
    }

    private IMergeUndoRepository Undo
        => _undo ?? throw new InvalidOperationException(
            "No IMergeUndoRepository is registered, so merges cannot be reversed. Register "
            + "Winnow.Data's MergeUndoRepository in the composition root.");

    private async Task<MergeRequest> RequestAsync(long candidateId, CancellationToken ct)
    {
        var candidate = await _candidates.GetAsync(candidateId, ct);
        return new MergeRequest
        {
            CandidateId = candidateId,
            AllowReleaseCollapse = !DiffersOnBundleEdition(candidate?.SignalsJson),
        };
    }

    /// <summary>
    /// True when the queued evidence recorded different bundle editions on the
    /// two sides. The payload is the evidence as it stood when the pair was
    /// queued, which is the evidence the user answered about.
    /// </summary>
    private static bool DiffersOnBundleEdition(string? signalsJson)
    {
        var payload = SoftMatchSignalsJson.Deserialize(signalsJson);
        if (payload is null)
        {
            return false;
        }

        return !payload.Left.BundleEditions.SequenceEqual(
            payload.Right.BundleEditions, StringComparer.Ordinal);
    }

    private void Log(MergeOutcome outcome)
    {
        if (!outcome.Applied)
        {
            _logger.LogInformation(
                "Merge candidate {CandidateId} not applied: {Blocker}.",
                outcome.Plan.CandidateId, outcome.Plan.Blocker);
            return;
        }

        _logger.LogInformation(
            "Merged candidate {CandidateId} as {Mode} (blocker {Blocker}): work {AbsorbedWork} into "
            + "{SurvivingWork}, release {AbsorbedRelease} into {SurvivingRelease}. Repointed "
            + "{Releases} release(s), {ExternalIds} external id(s), {Ownerships} ownership(s) with "
            + "{Folded} folded, {PlayRecords} play record(s), {Snapshots} snapshot(s), "
            + "{Sessions} session(s); {Duplicates} redundant row(s) dropped.",
            outcome.Plan.CandidateId, outcome.Plan.Mode, outcome.Plan.Blocker,
            outcome.Plan.AbsorbedWorkId, outcome.Plan.SurvivingWorkId,
            outcome.Plan.AbsorbedReleaseId, outcome.Plan.SurvivingReleaseId,
            outcome.Repointed.Releases, outcome.Repointed.ExternalIds,
            outcome.Repointed.Ownerships, outcome.Repointed.OwnershipsFolded,
            outcome.Repointed.PlayRecords, outcome.Repointed.PlaytimeSnapshots,
            outcome.Repointed.Sessions, outcome.Repointed.DuplicateRowsDropped);
    }
}
