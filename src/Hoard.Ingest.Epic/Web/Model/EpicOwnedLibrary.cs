using System.Globalization;
using Hoard.Core.Domain;
using Hoard.Core.Ingest;

namespace Hoard.Ingest.Epic.Web.Model;

/// <summary>
/// The unit of Epic's <c>totalTime</c> playtime figure.
///
/// <para><b>This exists because the unit is genuinely not established.</b> The
/// GraphQL schema declares <c>Playtime.totalTime</c> as a bare <c>Int!</c> with
/// no unit anywhere in the type, no open-source launcher reads the field at all
/// (Legendary has zero references to it; Heroic times the child process
/// instead), and Epic publishes no documentation for the endpoint. Seconds is
/// the plausible reading and it is the default, but it is a reading, not a
/// verified fact — so it is a setting the user can correct rather than a
/// constant compiled into a conversion.</para>
///
/// <para><b>What is at stake is smaller than it looks.</b> Hoard's staleness
/// buckets (§6.1) are a recency model, and Epic exposes no last-played timestamp
/// through any endpoint — the GraphQL type has no <c>lastPlayed</c>,
/// <c>firstPlayed</c>, <c>updatedAt</c> or <c>lastModified</c> field, each
/// individually confirmed absent. So the only bucket-relevant thing Epic's
/// playtime can say is <i>whether the game has ever been played</i>, and that bit
/// is unit-independent: a positive <c>totalTime</c> is positive in any unit. The
/// unit affects the number Hoard displays, not which bucket a game lands in.</para>
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
/// Title, when the response carried one. <b>Always null in practice</b> —
/// verified over 144 records on a real account, not one of which carried a name:
/// the library items endpoint returns identifiers, not display metadata. A null
/// here is the ingest contract's "this source has no title", which leaves the
/// local reader's name untouched.
///
/// <para><b>This is not the same as "Hoard cannot name an API-only title".</b>
/// The names come from <see cref="IEpicCatalogClient"/>, which is asked from
/// ENRICHMENT rather than from here — deliberately, and the split is the point.
/// Making that call during the ownership fetch would spend an authenticated
/// request per namespace on every sync to relearn names <c>catcache.bin</c>
/// already supplies for most of the library; making it from enrichment means it
/// is only asked about works that have no name, once each, cached for 30 days.
/// The layering also keeps the "local reader is authoritative" rule structural:
/// this candidate cannot carry a title, so it cannot overwrite one.</para>
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
    /// <see cref="TotalPlaytime"/> in minutes, or <b>null when Epic did not
    /// report playtime for this artifact at all</b>.
    ///
    /// <para><b>Null, never zero, and the distinction is the whole point.</b>
    /// Epic's playtime list only contains artifacts it has recorded time for; an
    /// owned game the user has never launched is simply absent from it. Absent
    /// therefore means "Epic has no figure", which is NOT the same as "the user
    /// has played this for zero minutes" — and it is emphatically not the same
    /// even when it looks it, because Epic's total only accrues from sessions the
    /// real Epic launcher started. A user who plays exclusively through Heroic,
    /// or through Hoard's own process monitor, accumulates nothing Epic-side.
    /// Reporting zero for those would be recording a source's silence as an
    /// answer, which is the failure this codebase has already been bitten by
    /// twice.</para>
    ///
    /// <para>A reported zero, on the other hand, is passed through as zero: Epic
    /// went to the trouble of having a record and the record says none, which is
    /// a real observation.</para>
    /// </summary>
    public long? PlaytimeMinutes(EpicPlaytimeUnit unit)
        => TotalPlaytime is not { } total
            ? null
            : unit == EpicPlaytimeUnit.Minutes
                ? total
                : total / 60;

    /// <summary>
    /// Projects onto the §5.1 ingest contract.
    ///
    /// <para><b><c>Installed</c> is null, and that is deliberate.</b> The library
    /// service reports entitlements; it cannot see the local disk, so it has no
    /// opinion on install state or install path. Null is how the ingest contract
    /// says "this source does not know", and it leaves whatever the local scan
    /// recorded untouched. Emitting <c>false</c> here is precisely the bug that
    /// emptied the install filter when the Steam Web API did it: these candidates
    /// may be resolved after the local ones, and a false would clear every
    /// install flag the manifests had just set. Which source wins must follow
    /// from which source <i>knows</i>, never from which was resolved last.</para>
    ///
    /// <para><b><c>LastPlayedAt</c> is null, and cannot be otherwise.</b> Epic
    /// exposes no last-played timestamp through any endpoint — confirmed field by
    /// field against the live GraphQL schema. This source can say how much, never
    /// when.</para>
    ///
    /// <para><c>AccountRef</c> is null rather than the Epic account id. The local
    /// Epic reader cannot attribute an account — the launcher's manifests live in
    /// <c>%PROGRAMDATA%</c> and are machine-wide — so filling it here would give
    /// the two sources different attribution for the same ownership. The merge
    /// takes the first real answer, so a non-null here would win and record an
    /// attribution the local half can never corroborate.</para>
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
/// The result of one authenticated library fetch: the account's owned Epic
/// titles, plus whether Epic actually answered.
///
/// <para><b><see cref="Succeeded"/> is the load-bearing field.</b> An empty
/// <see cref="Items"/> list means two completely different things depending on
/// it: "this account owns nothing" (answered) versus "not signed in, the refresh
/// token lapsed, Epic was unreachable, or the response would not parse"
/// (unanswered). Only the first is evidence about the library. A caller that
/// reconciles ownership must do nothing at all on an unanswered result — treating
/// one as an empty library would delete the user's entire Epic collection.</para>
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
