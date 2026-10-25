using Winnow.Covers;

namespace Winnow.App.Services;

/// <summary>Ranked local observations and source geometry; fetching remains a separate image request.</summary>
public sealed record BackdropArtworkCandidate(CoverKey Key, double AspectRatio, bool FitWholeHero);
public sealed record BackdropArtwork(IReadOnlyList<BackdropArtworkCandidate> Candidates, CoverKey? CoverKey);
