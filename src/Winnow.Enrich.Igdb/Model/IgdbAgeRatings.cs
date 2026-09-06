namespace Winnow.Enrich.Igdb.Model;

/// <summary>
/// The age-rating tokens IGDB reported for one game, already mapped to
/// <c>board:tier</c> form. Only games with at least one mappable rating appear;
/// a game with no age ratings or only ratings the client cannot name is absent.
/// </summary>
/// <param name="IgdbId">The IGDB game id the ratings belong to.</param>
/// <param name="RatingTokens">Distinct tokens in encounter order, e.g. <c>esrb:ao</c>, <c>pegi:18</c>.</param>
public sealed record IgdbAgeRatings(long IgdbId, IReadOnlyList<string> RatingTokens);
