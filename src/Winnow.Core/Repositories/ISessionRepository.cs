using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface ISessionRepository
{
    /// <summary>Inserts a session (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Session session, CancellationToken ct = default);

    /// <summary>Finds one open monitored sitting using exact ownership and process identities; ambiguous matches are refused.</summary>
    Task<Session?> FindOpenMonitoredAsync(long ownershipId,
        IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default);

    /// <summary>
    /// Atomically checkpoints or completes a monitored sitting and its process identities.
    /// Requires MonitorKey; retries preserve its row, notes, original attribution and known end.
    /// A matching open sitting is resumed without merging manual or legacy sessions.
    /// </summary>
    Task<Session> SaveMonitoredAsync(Session session,
        IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default);

    Task<Session?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>All sessions for an ownership, oldest first.</summary>
    Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>Inserts or replaces the (single) note for a session.</summary>
    Task SetNoteAsync(SessionNote note, CancellationToken ct = default);

    Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Saved journal responses for one ownership, newest session first.</summary>
    Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(
        long ownershipId, CancellationToken ct = default);

    /// <summary>Removes the note and optional rating for a session.</summary>
    Task DeleteNoteAsync(long sessionId, CancellationToken ct = default);
}
