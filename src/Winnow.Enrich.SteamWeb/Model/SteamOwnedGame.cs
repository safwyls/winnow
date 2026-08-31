using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;

namespace Winnow.Enrich.SteamWeb.Model;

/// <summary>One entry from <c>IPlayerService/GetOwnedGames</c> (§4.2).</summary>
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
/// <c>rtime_last_played</c> as UTC, or null when the field was absent or held
/// one of Steam's placeholders — <c>0</c>, or the <c>86400</c> (1970-01-02) the
/// local files write for a game last played before Steam tracked timestamps.
/// The rule is <see cref="Winnow.Core.Domain.SteamTime"/>, shared with the §4.1
/// readers so both sources call the same value unknown.
/// §4.2: returned <b>only when the API key belongs to the queried account</b> —
/// with a third party's key this is always null, which is why §4.1 makes local
/// files the primary playtime source.
/// </param>
/// <param name="IconHash">
/// <c>img_icon_url</c>: a bare content hash, not a URL. The conventional
/// community CDN path is
/// <c>https://media.steampowered.com/steamcommunity/public/images/apps/{appid}/{hash}.jpg</c>,
/// left unbuilt here because Winnow sources artwork from IGDB and that path has
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

    /// <summary>Projects onto the §5.1 ingest contract.</summary>
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
            ObservedAt: observedAt)
        {
            // This endpoint answers for ONE account — the one whose SteamID64 was
            // asked about — so it makes exactly one membership claim, and a
            // strong one. It is the only source that can see a licence the
            // account has never launched, which is what makes it, rather than
            // localconfig.vdf, the reason the account filter can be trusted:
            // without it the filter would only ever know about games somebody
            // had already played.
            Accounts = string.IsNullOrWhiteSpace(accountRef)
                ? []
                : [new CandidateAccount(accountRef, PlaytimeForeverMinutes, LastPlayedUtc)],
        };
}

/// <summary>
/// The result of one <c>GetOwnedGames</c> call: the account's library, plus
/// whether Steam actually answered.
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
    /// <param name="source">Provenance string for every candidate.</param>
    /// <param name="observedAt">
    /// Stamp for the observation, defaulting to <see cref="ObservedAt"/>.
    /// Callers feeding ingest pass the current time instead: on a cache hit
    /// <see cref="ObservedAt"/> is when the response was fetched, which can be
    /// hours old, and a backdated candidate would sit behind the newest stored
    /// row by <c>observed_at</c>, losing the resolver's latest-record
    /// comparison on every subsequent sync.
    /// </param>
    public IReadOnlyList<CandidateOwnership> ToCandidates(string source, DateTime? observedAt = null)
        => Games.Count == 0
            ? []
            : Games
                .Select(g => g.ToCandidate(SteamId.AccountRef, source, observedAt ?? ObservedAt))
                .ToArray();

    /// <summary>Diagnostics. Carries counts, never the key that fetched them.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SteamOwnedLibrary(steamid={SteamId}, succeeded={Succeeded}, games={Games.Count}, cached={FromCache})");
}
