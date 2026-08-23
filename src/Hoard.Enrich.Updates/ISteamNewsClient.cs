using Hoard.Enrich.Updates.Model;

namespace Hoard.Enrich.Updates;

/// <summary>
/// <c>ISteamNews/GetNewsForApp</c> — the announcement half of §4.5's pair, and
/// the cheap signal the whole polling strategy is built around (~440 bytes and
/// ~0.13 s per app, versus ~12.1 KB and ~0.77 s for a build lookup).
///
/// <para><b>Keyless.</b> Verified live: called with no <c>key=</c> it returns
/// 200. Combined with steamcmd.net being unauthenticated, that means M2 needs no
/// user-supplied credentials at all and no settings screen for them — so unlike
/// the IGDB client there is no "not configured" state to handle.</para>
///
/// <para>Every method is total: a 403, a shape change or a dead network yields
/// an outcome, never an exception (§5.1).</para>
/// </summary>
public interface ISteamNewsClient
{
    /// <summary>
    /// The newest patch note for one appid, or an outcome explaining why there
    /// isn't one.
    ///
    /// <para>One appid per request — the parameter is a required singular
    /// <c>uint32</c> per Valve's own <c>GetSupportedAPIList</c>, so there is no
    /// batching to design for. The request asks for <c>count=1</c> and
    /// <c>maxlength=1</c>, which is what keeps it at ~440 bytes: the endpoint has
    /// no "since" parameter (<c>enddate</c> pages backwards), so change detection
    /// is "fetch the newest and compare its date to a stored high-water
    /// mark".</para>
    ///
    /// <para>A cached <see cref="NewsOutcome.NoFeed"/> is answered without a
    /// request. Nothing else is cached here: the whole point of the call is to
    /// see whether the newest item has changed.</para>
    /// </summary>
    Task<NewsFetch> GetLatestPatchNoteAsync(string appId, CancellationToken ct = default);
}
