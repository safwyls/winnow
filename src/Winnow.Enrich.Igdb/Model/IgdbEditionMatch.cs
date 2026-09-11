using Winnow.Core.Identity;

namespace Winnow.Enrich.Igdb.Model;

public enum IgdbEditionMatchStatus { Missing, Ambiguous, NotEdition, Edition }

public sealed record IgdbEditionMatch(string Uid, IgdbEditionMatchStatus Status,
    long? EditionGameId, long? VersionParentId, string? VersionTitle,
    CachedEvidenceSource Source, DateTime ValidUntilUtc);
