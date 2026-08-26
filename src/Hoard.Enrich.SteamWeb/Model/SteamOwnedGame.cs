using System.Globalization;
using Hoard.Core.Domain;
using Hoard.Core.Ingest;

namespace Hoard.Enrich.SteamWeb.Model;

/// <summary>
/// One entry from <c>IPlayerService/GetOwnedGames</c> (§4.2).
///
/// <para>Everything the endpoint returns that an ingest source could want is
/// projected here — appid, name, both playtime figures, last-played, icon hash.
/// The full response body is also stored verbatim in <c>metadata_cache</c>, so
/// the per-platform playtime splits (<c>playtime_windows_forever</c> and
/// friends), <c>content_descriptorids</c> and anything Valve adds later are
/// recoverable without a refetch.</para>
/// </summary>
/// <param name="AppId">Steam appid, as a string, matching <c>ExternalId.ProviderId</c>.</param>
/// <param name="Title">
/// Name as Steam reports it, or null when the field was absent or blank.
/// <c>include_appinfo=1</c> is what makes this present at all — without it the
/// endpoint returns appids and playtime only.
/// </param>
/// <param name="PlaytimeForeverMinutes">
/// Cumulative playtime in minutes. <b>Zero is meaningful and common</b>: it is
/// how a never-launched owned game reports, which is the entire population
/// <c>localconfig.vdf</c> cannot see.
/// </param>
/// <param name="PlaytimeTwoWeeksMinutes">
/// Recent playtime in minutes. Steam omits <c>playtime_2weeks</c> entirely when
/// it is zero, so absent and zero are indistinguishable and both arrive as 0.
/// </param>
/// <param name="LastPlayedUtc">
/// <c>rtime_last_played</c> as UTC, or null when the field was absent or zero.
/// §4.2: returned <b>only when the API key belongs to the queried account</b> —
/// with a third party's key this is always null, which is why §4.1 makes local
/// files the primary playtime source.
/// </param>
/// <param name="IconHash">
/// <c>img_icon_url</c>: a bare content hash, not a URL. The conventional
/// community CDN path is
/// <c>https://media.steampowered.com/steamcommunity/public/images/apps/{appid}/{hash}.jpg</c>,
/// left unbuilt here because Hoard sources artwork from IGDB and that path has
/// not been verified against a live fetch.
/// </param>
public sealed record SteamOwnedGame(
    string AppId,
    string? Title,
    long PlaytimeForeverMinutes,
    long PlaytimeTwoWeeksMinutes,
    DateTime? LastPlayedUtc,
    string? IconHash)
{
    /// <summary>True when Steam reports no playtime at all for this appid on this account.</summary>
    public bool NeverPlayed => PlaytimeForeverMinutes <= 0 && LastPlayedUtc is null;

    /// <summary>
    /// Projects onto the §5.1 ingest contract.
    ///
    /// <para><b><c>Installed</c> is null, and that is the whole point.</b>
    /// <c>GetOwnedGames</c> reports licences; it cannot see the local disk, so it
    /// has no opinion on install state or install path. Null is how the ingest
    /// contract says "this source does not know" — see
    /// <see cref="CandidateOwnership.Installed"/> — and it leaves whatever the
    /// local scan (§4.1) recorded untouched. Emitting <c>false</c> here instead
    /// is the bug that emptied the "Installed" filter: these candidates are
    /// resolved after the local ones, so every sync cleared the install flags the
    /// appmanifests had just set. Never let this source clear a local answer.
    /// §4.1 likewise makes the local figure authoritative for playtime; the Web
    /// API's can lag behind a session Steam has not yet synced.</para>
    ///
    /// <para><c>AcquiredAt</c> stays null: <c>GetOwnedGames</c> does not expose a
    /// purchase or licence date in any form.</para>
    /// </summary>
    /// <param name="accountRef">Steam3 account id — <see cref="SteamId.AccountRef"/>, so the
    /// candidate attributes to the same account as its locally-scanned twin.</param>
    /// <param name="source">Provenance string; <see cref="SteamWebApiClient.SourceName"/> by default.</param>
    /// <param name="observedAt">When the response was observed (UTC).</param>
    public CandidateOwnership ToCandidate(string? accountRef, string source, DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: AppId,
            Title: Title,
            AccountRef: accountRef,
            InstallPath: null,
            Installed: null, // "cannot know", never "not installed".
            PlaytimeMinutes: PlaytimeForeverMinutes,
            LastPlayedAt: LastPlayedUtc,
            AcquiredAt: null,
            Source: source,
            ObservedAt: observedAt);
}

/// <summary>
/// The result of one <c>GetOwnedGames</c> call: the account's library, plus
/// whether Steam actually answered.
///
/// <para><b><see cref="Succeeded"/> is the load-bearing field.</b> An empty
/// <see cref="Games"/> list means two completely different things depending on
/// it: "this account owns nothing" (answered) versus "the request failed, or
/// Steam returned the bare <c>{"response":{}}</c> envelope it sends for a
/// profile it will not disclose" (unanswered). Only the first is evidence about
/// the library. A caller that reconciles ownership must do nothing at all on an
/// unanswered result — treating one as an empty library would delete the user's
/// entire collection.</para>
/// </summary>
/// <param name="SteamId">The account queried.</param>
/// <param name="Succeeded">Whether Steam returned a library, however small.</param>
/// <param name="Games">The games, ordered by appid. Empty on an unanswered result.</param>
/// <param name="ObservedAt">When the response was observed, or served from cache (UTC).</param>
/// <param name="FromCache">True when no request was made because a fresh cache entry answered.</param>
public sealed record SteamOwnedLibrary(
    SteamId SteamId,
    bool Succeeded,
    IReadOnlyList<SteamOwnedGame> Games,
    DateTime ObservedAt,
    bool FromCache)
{
    /// <summary>The unanswered result: no data, and explicitly not a claim that the library is empty.</summary>
    public static SteamOwnedLibrary Unanswered(SteamId steamId, DateTime observedAt)
        => new(steamId, Succeeded: false, Games: [], ObservedAt: observedAt, FromCache: false);

    /// <summary>Lookup by appid, for merging against another source's candidates.</summary>
    public IReadOnlyDictionary<string, SteamOwnedGame> ByAppId
        => Games.ToDictionary(static g => g.AppId, static g => g, StringComparer.Ordinal);

    /// <summary>How many entries carry a <c>rtime_last_played</c> timestamp (§4.2's key-ownership tell).</summary>
    public int WithLastPlayed => Games.Count(static g => g.LastPlayedUtc is not null);

    /// <summary>
    /// Projects the whole library onto the §5.1 ingest contract. See
    /// <see cref="SteamOwnedGame.ToCandidate"/> for the field caveats.
    /// </summary>
    public IReadOnlyList<CandidateOwnership> ToCandidates(string source)
        => Games.Count == 0
            ? []
            : Games
                .Select(g => g.ToCandidate(SteamId.AccountRef, source, ObservedAt))
                .ToArray();

    /// <summary>Diagnostics. Carries counts, never the key that fetched them.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SteamOwnedLibrary(steamid={SteamId}, succeeded={Succeeded}, games={Games.Count}, cached={FromCache})");
}
