using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The per-account membership rows behind the account-visibility filter
/// (migration 0015).
///
/// <para><b>Append and update only. Nothing here deletes.</b> A row says "this
/// account was observed holding this game", and a later scan that cannot see the
/// account — a second user signed out, an unreadable <c>userdata/</c>, a private
/// profile — has learned nothing that unsays it. Deleting on absence would make
/// the filter hide a game every time the other user logged out, which is the
/// single most confusing thing this feature could do.</para>
/// </summary>
public interface IOwnershipAccountRepository
{
    /// <summary>
    /// Records one account's view of one ownership, or folds it into the row
    /// already there.
    ///
    /// <para>Merge rules, all within the one account. A null incoming figure
    /// never overwrites a stored one — "this reader could not tell" is not a
    /// correction. Where both sides have a figure, minutes follow the err-low
    /// discipline rather than a max: a disagreement within
    /// <see cref="Domain.PlaytimeTolerance.Minutes"/> settles at the LOWER
    /// figure (the same answer the ownership series reaches, so a filtered
    /// library cannot report more than an unfiltered one); a larger rise is
    /// play and is recorded; a larger FALL is recorded only when the incoming
    /// last-played is at least as current as the stored one, which distinguishes
    /// a reader correcting its own count from one that has simply seen less.
    /// Last-played takes the later date, <c>source</c> and <c>last_seen_at</c>
    /// take the incoming observation, and <c>first_seen_at</c> is written once
    /// and never moved.</para>
    ///
    /// <para>These are refreshed current facts, not an append-only series —
    /// which is what gives a correction somewhere to land. A pure ratchet would
    /// make one spurious high reading permanent, and in filtered mode that is a
    /// wrong number on the tile and a wrong bucket under it.</para>
    /// </summary>
    Task UpsertAsync(OwnershipAccountUpsert row, CancellationToken ct = default);

    /// <summary>Every membership row for one ownership, ordered by account reference.</summary>
    Task<IReadOnlyList<OwnershipAccount>> GetByOwnershipAsync(
        long ownershipId, CancellationToken ct = default);

    /// <summary>
    /// Distinct account references seen for one store, ordered. Answers "which
    /// accounts is it worth asking the Web API about", including the accounts
    /// that never won <c>ownerships.account_ref</c> and are therefore invisible
    /// to a query over that column.
    /// </summary>
    Task<IReadOnlyList<string>> GetAccountRefsAsync(string store, CancellationToken ct = default);
}
