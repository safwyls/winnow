namespace Winnow.Enrich.GamesDb.Model;

/// <summary>Platform keys gamesdb uses. Same vocabulary as Galaxy's <c>Platforms</c> table.</summary>
public static class GamesDbPlatforms
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
}

/// <summary>
/// What one lookup produced: the id of the underlying game and every store
/// release gamesdb knows shares it. Resolves games, not editions.
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
    /// lists no release there. Prefer <see cref="IdsOn"/> where the id has a
    /// known shape, since the graph can carry junk entries.
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
