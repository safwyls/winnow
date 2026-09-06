using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;

namespace Winnow.App.Services;

/// <summary>
/// Adapter over <see cref="IgdbManualAssignment"/>.
/// <see cref="IgdbManualAssignment.SearchAsync"/>,
/// <see cref="IgdbManualAssignment.GetByIdAsync"/> and
/// <see cref="IgdbManualAssignment.AssignAsync"/> already soft-fail; the
/// two pass-through reads (<see cref="ClearAsync"/> and
/// <see cref="GetPinAsync"/>) do not, so they are caught here. No call on
/// this seam throws at a view model, which is what lets the modal's control
/// render a state for every outcome instead of a crash for one of them.
/// </summary>
public sealed class IgdbAssignmentService : IIgdbAssignmentService
{
    private readonly IgdbManualAssignment _assignment;
    private readonly IWorkRepository? _works;
    private readonly ILogger<IgdbAssignmentService> _log;

    public IgdbAssignmentService(
        IgdbManualAssignment assignment,
        IWorkRepository? works = null,
        ILogger<IgdbAssignmentService>? log = null)
    {
        _assignment = assignment;
        _works = works;
        _log = log ?? NullLogger<IgdbAssignmentService>.Instance;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IgdbCandidate>> SearchAsync(
        string title, CancellationToken ct = default)
    {
        var results = await _assignment.SearchAsync(title, ct: ct);

        return
        [
            .. results.Select(r => new IgdbCandidate(
                r.IgdbId, r.Name, r.CoverUrl, r.FirstReleaseYear, r.Platforms)),
        ];
    }

    /// <inheritdoc/>
    public async Task<IgdbCandidate?> GetCandidateByIdAsync(
        long igdbId, CancellationToken ct = default)
    {
        var result = await _assignment.GetByIdAsync(igdbId, ct);

        return result is null
            ? null
            : new IgdbCandidate(
                result.IgdbId, result.Name, result.CoverUrl, result.FirstReleaseYear, result.Platforms);
    }

    /// <inheritdoc/>
    public async Task<IgdbAssignmentOutcome> AssignAsync(
        long workId, long igdbId, CancellationToken ct = default)
    {
        var result = await _assignment.AssignAsync(workId, igdbId, ct);

        return result.Status switch
        {
            IgdbAssignmentStatus.Assigned => IgdbAssignmentOutcome.Assigned,
            IgdbAssignmentStatus.WorkNotFound => IgdbAssignmentOutcome.WorkNotFound,
            IgdbAssignmentStatus.MetadataUnavailable => IgdbAssignmentOutcome.MetadataUnavailable,
            IgdbAssignmentStatus.IgdbIdClaimedByAnotherWork =>
                IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            _ => IgdbAssignmentOutcome.Failed,
        };
    }

    /// <inheritdoc/>
    public async Task<IgdbClaimingGame?> FindClaimingGameAsync(
        long igdbId, CancellationToken ct = default)
    {
        if (_works is null || igdbId <= 0)
        {
            return null;
        }

        try
        {
            var holder = await _works.GetByIgdbIdAsync(igdbId, ct);

            return holder is null
                ? null
                : new IgdbClaimingGame(
                    holder.Id, holder.Name, holder.CoverUrl, holder.FirstReleaseYear);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed read degrades to "nobody holds it", which draws the
            // bare refusal sentence rather than an offer naming a wrong game.
            _log.LogWarning(ex, "Reading the work holding IGDB entry {IgdbId} failed.", igdbId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ClearAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            return await _assignment.ClearAsync(workId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Clearing the IGDB pin on work {WorkId} failed.", workId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            return await _assignment.GetPinAsync(workId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the IGDB pin on work {WorkId} failed.", workId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _assignment.GetLivePinnedWorkIdsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed read degrades to "nothing is pinned" — the
            // store-capsule precedence the library had before pins existed.
            _log.LogWarning(ex, "Reading the live IGDB pins failed.");
            return new HashSet<long>();
        }
    }
}
