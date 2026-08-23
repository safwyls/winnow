namespace Hoard.Core.Domain;

/// <summary>
/// Optional user-authored journal entry for a <see cref="Session"/>.
/// At most one note per session; rating is 1–5 when present.
/// </summary>
public sealed record SessionNote
{
    public required long SessionId { get; init; }
    public string? Note { get; init; }
    public int? Rating { get; init; }
}
