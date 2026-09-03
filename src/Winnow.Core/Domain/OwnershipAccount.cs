namespace Winnow.Core.Domain;

/// <summary>
/// One row of <c>ownership_accounts</c>: the fact that a named account holds or
/// has played an ownership, with that account's own figures.
///
/// <para>The table this projects onto exists because <c>ownerships.account_ref</c>
/// cannot answer "does account A own this?". That column holds the winner of the
/// play tuple — one account per (release, store) — so a game two people own
/// reports whichever of them played it more, and the other one is invisible to
/// any question asked of that column. These rows are the per-account form of the
/// same observation, and the account-visibility filter is decided from them.</para>
/// </summary>
/// <param name="OwnershipId">The ownership these figures belong to.</param>
/// <param name="AccountRef">
/// The source's account reference — a Steam3 account id, a GOG user id. Never
/// blank: a row that names no account is evidence about nobody.
/// </param>
/// <param name="PlaytimeMinutes">
/// This account's cumulative minutes, or null when the source knows the account
/// holds the app but not for how long. Null is not zero: an owned-but-never-
/// launched game reports zero, and "not measured" reports null.
/// </param>
/// <param name="LastPlayedAt">This account's last-played timestamp (UTC), or null when unknown.</param>
/// <param name="Source">
/// Which reader last reported this membership. Load-bearing rather than
/// decorative: the seed migration 0015 writes
/// <see cref="OwnershipAccountSources.LegacyOwnershipColumn"/>, and the filter
/// refuses to hide a game on the strength of a seeded row alone, because a seed
/// carries exactly the single-winner ambiguity the table exists to replace.
/// </param>
/// <param name="ObservedAt">
/// When the reader saw this. Written to <c>first_seen_at</c> on insert and to
/// <c>last_seen_at</c> on every write. <c>first_seen_at</c> never moves — it is
/// the earliest moment Winnow could prove this account holds this game, and a
/// re-observation is not a new fact.
/// </param>
public sealed record OwnershipAccountUpsert(
    long OwnershipId,
    string AccountRef,
    long? PlaytimeMinutes,
    DateTime? LastPlayedAt,
    string Source,
    DateTime ObservedAt);

/// <summary>A stored <c>ownership_accounts</c> row, read back whole.</summary>
public sealed record OwnershipAccount
{
    public required long OwnershipId { get; init; }
    public required string AccountRef { get; init; }
    public long? PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Source { get; init; }
    public required DateTime FirstSeenAt { get; init; }
    public required DateTime LastSeenAt { get; init; }
}

/// <summary>
/// <c>ownership_accounts.source</c> values that mean something to code rather
/// than only to a support question. Reader names (<c>steam_local</c>,
/// <c>steam_web</c>, …) pass through from the candidate and are not listed here.
/// </summary>
public static class OwnershipAccountSources
{
    /// <summary>
    /// Written by migration 0015's seed, and by nothing else. Marks a row
    /// derived from the old single-winner <c>ownerships.account_ref</c> column
    /// rather than from a reader that enumerated accounts.
    ///
    /// <para>A seeded row is <b>not</b> evidence about who does not own a game.
    /// It names the account that won the play tuple, which on a shared game is
    /// routinely not the only owner — so treating it as a complete account list
    /// would hide exactly the games acceptance criterion #2 forbids hiding. The
    /// bucket query therefore requires at least one non-seed row before it will
    /// hide anything, and the first sync after the migration supplies them.</para>
    /// </summary>
    public const string LegacyOwnershipColumn = "ownerships.account_ref";
}
