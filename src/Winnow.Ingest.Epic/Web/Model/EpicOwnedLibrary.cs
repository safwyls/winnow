using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;

namespace Winnow.Ingest.Epic.Web.Model;

/// <summary>
/// The unit of Epic's <c>totalTime</c> playtime figure. Unverified; exposed as a
/// setting the user can correct rather than a compiled constant.
/// </summary>
public enum EpicPlaytimeUnit
{
    /// <summary>The default reading. Unverified — see the type remarks.</summary>
    Seconds = 0,

    /// <summary>The alternative, if verification against the launcher's own display shows a 60× discrepancy.</summary>
    Minutes = 1,
}

/// <summary>
/// One entry from <c>/library/api/public/items</c>: an owned Epic artifact.
/// </summary>
/// <param name="CatalogItemId">
/// Epic's catalog item id — the same value <c>catcache.bin</c> stores as
/// <c>id</c> and the <c>.item</c> manifest as <c>CatalogItemId</c>. This is
/// <see cref="CandidateOwnership.ProviderId"/>, chosen so an API candidate and
/// its locally-scanned twin land on the same ownership and
/// <see cref="CandidateOwnershipMerge"/> collapses them.
/// </param>
/// <param name="AppName">
/// Epic's per-artifact codename ("Bluebird" is Fez) — <c>releaseInfo[].appId</c>
/// locally. <b>Never a title</b>, and never rendered. It is carried for exactly
/// one reason: the playtime endpoint keys on it, as <c>artifactId</c>.
/// </param>
/// <param name="Namespace">Epic sandbox/namespace id, or null. Carried for cross-store identity work.</param>
/// <param name="Title">
/// Title, when the response carried one. Always null in practice; names come
/// from <see cref="IEpicCatalogClient"/> during enrichment instead.
/// </param>
/// <param name="AcquiredAt">
/// <c>acquisitionDate</c> as UTC, or null. <b>This is the one field the API can
/// answer and the local files cannot</b> — nothing on disk records when the user
/// claimed a title, as the local spike established.
/// </param>
/// <param name="TotalPlaytime">
/// Raw <c>totalTime</c> from the playtime endpoint, or null when Epic reported
/// none for this artifact. Raw and unconverted; see <see cref="EpicPlaytimeUnit"/>.
/// </param>
public sealed record EpicLibraryItem(
    string CatalogItemId,
    string AppName,
    string? Namespace,
    string? Title,
    DateTime? AcquiredAt,
    long? TotalPlaytime)
{
    /// <summary>
    /// <see cref="TotalPlaytime"/> in minutes, or null when Epic reported no
    /// playtime. Null means "no data", not "zero minutes played".
    /// </summary>
    public long? PlaytimeMinutes(EpicPlaytimeUnit unit)
        => TotalPlaytime is not { } total
            ? null
            : unit == EpicPlaytimeUnit.Minutes
                ? total
                : total / 60;

    /// <summary>
    /// Projects onto the ingest contract. Installed, LastPlayedAt, and AccountRef
    /// are null because this source cannot know them.
    /// </summary>
    public CandidateOwnership ToCandidate(string source, EpicPlaytimeUnit unit, DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Epic,
            ProviderId: CatalogItemId,
            Title: Title,
            AccountRef: null,
            InstallPath: null,
            Installed: null, // "cannot know", never "not installed".
            PlaytimeMinutes: PlaytimeMinutes(unit),
            LastPlayedAt: null, // Epic exposes none, anywhere.
            AcquiredAt: AcquiredAt,
            Source: source,
            ObservedAt: observedAt);
}

/// <summary>
/// The result of one authenticated library fetch. <see cref="Succeeded"/> distinguishes
/// "owns nothing" from "unanswered"; callers must not treat an unanswered result as empty.
/// </summary>
/// <param name="Succeeded">Whether Epic returned a library, however small.</param>
/// <param name="Items">The owned artifacts, ordered by catalog item id. Empty on an unanswered result.</param>
/// <param name="ObservedAt">When the response was observed, or served from cache (UTC).</param>
/// <param name="FromCache">True when no request was made because a fresh cache entry answered.</param>
/// <param name="PlaytimeAnswered">
/// Whether the playtime endpoint answered on this pass. False means every
/// item's playtime is null because it was not asked or not told — never because
/// the games are unplayed.
/// </param>
public sealed record EpicOwnedLibrary(
    bool Succeeded,
    IReadOnlyList<EpicLibraryItem> Items,
    DateTime ObservedAt,
    bool FromCache,
    bool PlaytimeAnswered)
{
    /// <summary>The unanswered result: no data, and explicitly not a claim that the library is empty.</summary>
    public static EpicOwnedLibrary Unanswered(DateTime observedAt)
        => new(Succeeded: false, Items: [], ObservedAt: observedAt, FromCache: false, PlaytimeAnswered: false);

    /// <summary>How many items carry a playtime figure — the §10-style tell that the playtime call worked.</summary>
    public int WithPlaytime => Items.Count(static i => i.TotalPlaytime is not null);

    /// <summary>Lookup by catalog item id, for merging against the local reader's candidates.</summary>
    public IReadOnlyDictionary<string, EpicLibraryItem> ByCatalogItemId
        => Items.ToDictionary(static i => i.CatalogItemId, static i => i, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Projects the whole library onto the §5.1 ingest contract. See
    /// <see cref="EpicLibraryItem.ToCandidate"/> for the field caveats.
    /// </summary>
    public IReadOnlyList<CandidateOwnership> ToCandidates(
        string source, EpicPlaytimeUnit unit, DateTime? observedAt = null)
        => Items.Count == 0
            ? []
            : Items.Select(i => i.ToCandidate(source, unit, observedAt ?? ObservedAt)).ToArray();

    /// <summary>Diagnostics. Carries counts, never the account they belong to.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"EpicOwnedLibrary(succeeded={Succeeded}, items={Items.Count}, withPlaytime={WithPlaytime}, cached={FromCache})");
}
