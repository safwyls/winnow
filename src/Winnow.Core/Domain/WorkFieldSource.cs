namespace Winnow.Core.Domain;

/// <summary>
/// One stored row from <c>work_field_sources</c> (migration 0027): the
/// source that last wrote a single metadata field on a work.
/// </summary>
public sealed record WorkFieldSource
{
    public required long WorkId { get; init; }

    public required string Field { get; init; }

    public required string Source { get; init; }

    public required DateTime SetAt { get; init; }
}

/// <summary>
/// A field's current value paired with its source, for display in the
/// editor. A null <see cref="Source"/> means no writer has claimed the
/// field — the value is on automatic, not "unknown".
/// </summary>
public sealed record WorkFieldState
{
    public required string Field { get; init; }

    public string? Value { get; init; }

    public string? Source { get; init; }

    public bool IsUserOwned => Queries.FieldSources.IsUserOwned(Source);
}

/// <summary>
/// What a field edit did. Every refusal is a returned value the UI
/// renders with its own sentence, never an exception — the same
/// arrangement <see cref="WorkIgdbPinOutcome"/> has.
/// </summary>
public enum WorkFieldEditOutcome
{
    Applied,

    /// <summary>The field name is not in <see cref="Queries.WorkFields.All"/> and never reached SQL.</summary>
    UnknownField,

    WorkNotFound,

    /// <summary>A blank name (<c>works.name</c> is NOT NULL), or a year that will not parse or is out of the 1900–2200 range.</summary>
    InvalidValue,
}
