namespace Winnow.Enrich.GamesDb.Model;

/// <summary>Platform keys gamesdb uses. Same vocabulary as Galaxy's <c>Platforms</c> table.</summary>
public static class GamesDbPlatforms
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
}

/// <summary>
/// What one lookup produced: the id of the underlying <i>game</i> and every
/// store release gamesdb knows shares it.
///
/// <para><b>This resolves games, not editions, and that distinction is load
/// bearing.</b> <c>steam_224760</c> and <c>gog_1207659211</c> collapse to one
/// <see cref="GameId"/>, which is the right granularity for a Work and the
/// wrong one for a Release — §5.3's four-layer model and §9's pitfall 5 (Skyrim
/// Special Edition is not Skyrim) both still apply. Winnow uses this to find
/// <i>metadata</i> for a title it could not otherwise look up, and never to
/// decide that two releases are the same release.</para>
/// </summary>
/// <param name="Platform">The platform the lookup was made under.</param>
/// <param name="ExternalId">The id the lookup was made with.</param>
/// <param name="GameId">gamesdb's own id for the underlying game.</param>
/// <param name="Releases">Every store release sharing that game id, including the one asked about.</param>
public sealed record GamesDbGame(
    string Platform,
    string ExternalId,
    string GameId,
    IReadOnlyList<GamesDbRelease> Releases)
{
    /// <summary>
    /// The first id under <paramref name="platform"/>, or null when gamesdb
    /// lists no release there.
    ///
    /// <para>Null is a fact about the game — an Epic exclusive genuinely has no
    /// Steam twin — and callers must treat it as "no route this way", never as
    /// "the lookup failed".</para>
    ///
    /// <para><b>Prefer <see cref="IdsOn"/> where the id has a known shape.</b>
    /// The graph is crowd-shaped and carries junk: Fez lists <c>steam/224760</c>
    /// and also <c>steam/steam_224760</c>, the release key pasted into the id
    /// field. Order across duplicates is not guaranteed, so a caller that knows
    /// what a valid id looks like should filter rather than take the first.</para>
    /// </summary>
    public string? IdOn(string platform)
    {
        foreach (var release in Releases)
        {
            if (string.Equals(release.Platform, platform, StringComparison.Ordinal)
                && release.ExternalId.Length > 0)
            {
                return release.ExternalId;
            }
        }

        return null;
    }

    /// <summary>
    /// Every id listed under <paramref name="platform"/>, in the order the graph
    /// returned them — so a caller can apply its own idea of a well-formed id
    /// instead of trusting the first row.
    /// </summary>
    public IReadOnlyList<string> IdsOn(string platform)
    {
        var ids = new List<string>();
        foreach (var release in Releases)
        {
            if (string.Equals(release.Platform, platform, StringComparison.Ordinal)
                && release.ExternalId.Length > 0)
            {
                ids.Add(release.ExternalId);
            }
        }

        return ids;
    }
}

/// <summary>One store's release of a game.</summary>
/// <param name="Platform"><c>steam</c>, <c>gog</c>, <c>epic</c>, <c>psn</c>, …</param>
/// <param name="ExternalId">That store's id — a Steam appid, a GOG product id, an Epic <c>AppName</c>.</param>
public sealed record GamesDbRelease(string Platform, string ExternalId);
