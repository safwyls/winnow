using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface ISessionRepository
{
    /// <summary>Inserts a session (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Session session, CancellationToken ct = default);

    Task<Session?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>All sessions for an ownership, oldest first.</summary>
    Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>Inserts or replaces the (single) note for a session.</summary>
    Task SetNoteAsync(SessionNote note, CancellationToken ct = default);

    Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default);
}
