namespace Winnow.Core.Ingest;

/// <summary>
/// The normalised record every <c>Ingest.*</c> module emits (§5.1). The resolver
/// maps candidates onto Work/Release/Ownership; ingest must never write those directly.
/// </summary>
/// <param name="Provider">Source platform key (e.g. "steam") — see <see cref="Domain.ExternalIdProviders"/>.</param>
/// <param name="ProviderId">Platform-native id (Steam appid, GOG product id, Epic catalog item id).</param>
/// <param name="Title">Raw title. Null means "known to exist but unnamed" (provisional).</param>
/// <param name="AccountRef">Opaque source account reference (e.g. Steam3 id), if known.</param>
/// <param name="InstallPath">Local install directory. Meaningful only when <paramref name="Installed"/> is non-null.</param>
/// <param name="Installed">Three-valued: true/false when source inspected disk, null when it cannot know.</param>
/// <param name="PlaytimeMinutes">Cumulative playtime, if the source exposes it.</param>
/// <param name="LastPlayedAt">Last-played timestamp (UTC), if the source exposes it.</param>
/// <param name="AcquiredAt">Acquisition timestamp (UTC), if the source exposes it.</param>
/// <param name="Source">Which reader produced this (e.g. "steam_local") -- kept for provenance.</param>
/// <param name="ObservedAt">When the reader observed this state (UTC).</param>
public sealed record CandidateOwnership(
    string Provider,
    string ProviderId,
    string? Title,
    string? AccountRef,
    string? InstallPath,
    bool? Installed,
    long? PlaytimeMinutes,
    DateTime? LastPlayedAt,
    DateTime? AcquiredAt,
    string Source,
    DateTime ObservedAt)
{
    /// <summary>
    /// Every account this reader saw holding or playing the app, with that
    /// account's own figures — not the collapsed household answer the columns
    /// above carry.
    ///
    /// <para><see cref="AccountRef"/> above names ONE account: the one that won
    /// the play tuple. On a PC with two accounts signed in, a game both people
    /// own carries whichever of them played it more, and asking "does account A
    /// own this?" of that single column can only answer wrongly for the account
    /// that lost. This list is the honest form of the same question, and is what
    /// the account-visibility filter is decided from.</para>
    ///
    /// <para>Empty is the ordinary answer for a source that cannot enumerate
    /// accounts at all — GOG's machine-wide install registry, every Epic
    /// reader. Empty is "not known", never "belongs to nobody", and the filter
    /// treats it that way: a row with no per-account evidence stays visible.</para>
    /// </summary>
    public IReadOnlyList<CandidateAccount> Accounts { get; init; } = [];
}

/// <summary>
/// One account's own view of one app: what THIS account played, as opposed to
/// what the machine as a whole did.
///
/// <para>Carries no source or observation time of its own — those come from the
/// <see cref="CandidateOwnership"/> this entry arrived on, because an entry
/// cannot have been observed by a different reader at a different moment than
/// the candidate carrying it.</para>
/// </summary>
/// <param name="AccountRef">
/// The source's opaque account reference — a Steam3 account id, a GOG user id.
/// The same string the matching <see cref="CandidateOwnership.AccountRef"/>
/// would carry, so the two agree when one account is all there is.
/// </param>
/// <param name="PlaytimeMinutes">
/// This account's cumulative minutes, or null when the source knows the account
/// holds the app but not for how long. Null is not zero.
/// </param>
/// <param name="LastPlayedAt">
/// This account's last-played timestamp (UTC), or null when unknown. A null
/// beside real minutes is Steam's pre-timestamp sentinel, not "never played".
/// </param>
public sealed record CandidateAccount(
    string AccountRef,
    long? PlaytimeMinutes,
    DateTime? LastPlayedAt);
