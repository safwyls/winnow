namespace Winnow.App.Services;

public interface ISessionJournalService
{
    event EventHandler<EndedSession>? SessionEnded;
    bool PromptEnabled { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task SetPromptEnabledAsync(bool enabled, CancellationToken ct = default);
    Task SaveAsync(long sessionId, string? note, int? rating, CancellationToken ct = default);
}
/// <summary>A finished sitting, as the prompt needs it.</summary>
/// <param name="SessionId">The row the note would be attached to.</param>
/// <param name="OwnershipId">Which game, so the prompt can name it.</param>
/// <param name="DurationSeconds">How long it ran.</param>
public readonly record struct EndedSession(long SessionId, long OwnershipId, long DurationSeconds);
