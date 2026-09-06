using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.Services;

/// <summary>One field in the editor's read model: value plus source.</summary>
public sealed record WorkMetadataField(string Field, string? Value, string? Source)
{
    public bool IsUserOwned => Winnow.Core.Queries.FieldSources.IsUserOwned(Source);
}

/// <summary>Everything the editor needs to render one work's metadata panel.</summary>
public sealed record WorkMetadataSnapshot(
    long WorkId,
    string Title,
    bool IsPinned,
    IReadOnlyList<WorkMetadataField> Fields);

/// <summary>
/// What an art import did. Every refusal is a returned value the editor
/// renders with its own sentence, never an exception.
/// </summary>
public enum WorkArtEditOutcome
{
    Applied,

    UnknownField,

    WorkNotFound,

    FileNotFound,

    Unreadable,

    TooLarge,

    NotAnImage,

    BadUrl,

    DownloadFailed,

    Failed,
}

/// <summary>
/// App-layer seam in front of the field-source repositories and
/// <see cref="UserArtStore"/>, the same arrangement
/// <see cref="IIgdbAssignmentService"/> has. Nothing on this interface
/// throws at a view model: every refusal is a returned value the editor
/// renders with its own sentence.
/// </summary>
public interface IWorkMetadataEditService
{
    Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default);

    Task<WorkFieldEditOutcome> SetFieldAsync(
        long workId, string field, string? value, CancellationToken ct = default);

    Task<WorkFieldEditOutcome> ResetFieldAsync(
        long workId, string field, CancellationToken ct = default);

    Task<WorkArtEditOutcome> SetArtFromFileAsync(
        long workId, string field, string filePath, CancellationToken ct = default);

    Task<WorkArtEditOutcome> SetArtFromUrlAsync(
        long workId, string field, string url, CancellationToken ct = default);

    /// <summary>
    /// Resolves a stored art URL to a <see cref="CoverKey"/> so the editor
    /// can preview art without the view model learning the URL shapes.
    /// </summary>
    CoverKey? ArtKeyFor(string? value);
}
