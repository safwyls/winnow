using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// The two endpoints M5's historical backfill reads:
/// <c>IPlayerService/ClientGetLastPlayedTimes</c> for the cumulative anchor and
/// the first-played dates, and <c>ISaleFeatureService/GetUserYearInReview</c>
/// for the per-month longitudinal series.
///
/// <para>Background only. Both calls are network work on the enrichment path and
/// neither may appear in front of a user (§5.1); the client never throws for an
/// unconfigured key or an unreachable Steam, and answers with an explicit
/// "unanswered" instead.</para>
/// </summary>
public interface ISteamHistoryClient
{
    /// <summary>
    /// Whether a user-supplied API key was found. False is the ordinary state
    /// for a user who has not entered one, not an error.
    /// </summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Per-app cumulative playtime and first/last played times for the account
    /// the API key belongs to.
    ///
    /// <para>Takes no <c>steamid</c>. Verified live 2026-08-28, the key alone
    /// identifies the account. That is also why the result is cached under a
    /// key-scoped entry rather than a per-account one.</para>
    /// </summary>
    /// <param name="purpose">Which credential kind this call should prefer. Defaults to
    /// <see cref="SteamCredentialPurpose.Unattended"/>, which is what every caller today
    /// is (background backfill). A caller a person is waiting on passes
    /// <see cref="SteamCredentialPurpose.UserInitiated"/>.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamWebOptions.CacheTtl"/>. Zero or less forces a refetch.</param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<SteamLastPlayedTimes> GetLastPlayedTimesAsync(
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default);

    /// <summary>
    /// One year of Steam Replay for one account. Coverage starts at 2022 (the
    /// first year Valve ran it); earlier years answer empty.
    /// </summary>
    /// <param name="steamId">The account to ask about.</param>
    /// <param name="year">Calendar year.</param>
    /// <param name="purpose">Which credential kind this call should prefer. Defaults to
    /// <see cref="SteamCredentialPurpose.Unattended"/>. A caller a person is waiting on
    /// passes <see cref="SteamCredentialPurpose.UserInitiated"/>.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamWebOptions.CacheTtl"/>. Zero or less forces a refetch.</param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<SteamYearInReview> GetYearInReviewAsync(
        SteamId steamId,
        int year,
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default);
}
