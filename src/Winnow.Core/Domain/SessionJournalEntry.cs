namespace Winnow.Core.Domain;

/// <summary>
/// A saved journal response together with the session it describes. The date is
/// the session's end when known, otherwise its start.
/// </summary>
public sealed record SessionJournalEntry
{
    public required long SessionId { get; init; }
    public required long OwnershipId { get; init; }
    public required DateTime SessionAt { get; init; }
    public string? Note { get; init; }
    public int? Rating { get; init; }
}
