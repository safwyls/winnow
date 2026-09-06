using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The seam for per-field provenance (migration 0027).
/// <see cref="SetFieldAsync"/> sets one field and makes the user its source.
/// <see cref="ResetFieldAsync"/> hands one field back to automatic.
/// </summary>
public interface IWorkFieldSourceRepository
{
    Task<IReadOnlyDictionary<string, string>> GetSourcesAsync(
        long workId, CancellationToken ct = default);

    /// <summary>
    /// Returns one entry per <see cref="Winnow.Core.Queries.WorkFields.All"/> even when
    /// no <c>work_field_sources</c> row exists for a field, so the editor
    /// renders every field and its state in one read.
    /// </summary>
    Task<IReadOnlyList<WorkFieldState>> GetStateAsync(
        long workId, CancellationToken ct = default);

    /// <summary>
    /// Sets one field on the work and stamps it with <c>user</c> as its
    /// source. The column name never comes from the caller — an unrecognised
    /// field is <see cref="WorkFieldEditOutcome.UnknownField"/> before any
    /// SQL is built.
    /// </summary>
    Task<WorkFieldEditOutcome> SetFieldAsync(
        long workId, string field, string? value, CancellationToken ct = default);

    /// <summary>
    /// Hands one field back to automatic. Deletes the stamp and empties the
    /// column, because the automatic pass is fill-only: leaving the value
    /// there would mean the field never came back. <c>name</c> is the
    /// exception — it sets <c>name_is_provisional = 1</c> and leaves the
    /// text standing as the placeholder the next pass will promote over.
    /// </summary>
    Task<WorkFieldEditOutcome> ResetFieldAsync(
        long workId, string field, CancellationToken ct = default);
}
