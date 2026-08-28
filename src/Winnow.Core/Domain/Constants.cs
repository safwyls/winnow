namespace Winnow.Core.Domain;

/// <summary>Valid <see cref="ExternalId.Provider"/> values (CHECK-constrained in the schema).</summary>
public static class ExternalIdProviders
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
    public const string Igdb = "igdb";

    /// <summary>
    /// The providers a store ingest writes — every provider except
    /// <see cref="Igdb"/>, which is Winnow's own canonical identity rather than
    /// something a user owns a copy on.
    ///
    /// <para><b>This list exists because asking about one store at a time is how
    /// a whole population goes missing.</b> Enrichment spent its life calling
    /// <c>GetEnrichmentTargetsAsync(Steam)</c>; the 67 Epic and 14 GOG rows in
    /// the author's library were never once asked about, and measured zero
    /// <c>igdb_id</c>, zero covers, zero years and zero summaries — not partial,
    /// exactly zero, which is the signature of a query that never ran rather
    /// than a source that had nothing to say. Any sweep over "the library"
    /// should iterate this, and a new store becomes visible to every such sweep
    /// by being added here once.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Stores = [Steam, Gog, Epic];
}

/// <summary>Valid <see cref="UpdateEvent.Kind"/> values (CHECK-constrained in the schema).</summary>
public static class UpdateEventKinds
{
    /// <summary>Depot build push (appinfo <c>depots.branches.public.timeupdated</c>). Noisy alone.</summary>
    public const string BuildPush = "build_push";

    /// <summary>Community announcement via <c>ISteamNews/GetNewsForApp</c>. Noisy alone.</summary>
    public const string Announcement = "announcement";
}

/// <summary>Valid <see cref="MergeCandidate.Status"/> values (CHECK-constrained in the schema).</summary>
public static class MergeCandidateStatuses
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
}

/// <summary>Valid <see cref="Session.DetectionMethod"/> values (CHECK-constrained in the schema).</summary>
public static class DetectionMethods
{
    /// <summary>Tier 1/2 process watching (§5.2 A).</summary>
    public const string ProcessWatch = "process_watch";

    /// <summary>Launch-option wrapper, exact timestamps (§5.2 B).</summary>
    public const string Wrapper = "wrapper";

    /// <summary>Backfilled from an import (e.g. GDPR export).</summary>
    public const string Import = "import";

    /// <summary>Entered by hand.</summary>
    public const string Manual = "manual";
}

/// <summary>
/// Valid <see cref="Session.AttributedBy"/> values (CHECK-constrained in the
/// schema, and nullable there because every session recorded before M3b
/// predates the column).
///
/// <para><b>This is the axis M3b exists to add, and it is not a quality score
/// — it is a record of what was known.</b> §5.2 lists the ways attribution by
/// inference goes wrong: launchers spawn children, games relaunch through a
/// second executable, an engine ships the same <c>Game.exe</c> name as three
/// other titles, and a process whose main module cannot be read has no path to
/// join on at all. Every one of those is a case where the watcher has to pick
/// between candidates. When Winnow fired the launch URI itself there is nothing
/// to pick between: the app already knows which ownership the user clicked, and
/// the intent hands the watcher that answer instead of making it guess.</para>
///
/// <para>Stored rather than derived because it cannot be reconstructed later:
/// nothing in a finished session says whether a human clicked Play in Winnow or
/// in Steam. A recommender that eventually wants to weight exact sessions above
/// inferred ones can only do that if the distinction was written down at the
/// time.</para>
/// </summary>
public static class SessionAttributions
{
    /// <summary>
    /// Winnow fired the launch and a process appeared while that intent was
    /// live. The ownership is the one the user clicked, not one resolved from a
    /// path.
    /// </summary>
    public const string Launch = "launch";

    /// <summary>
    /// Inferred: the running executable's path fell inside an ownership's
    /// install directory, or its name matched exactly one owned game. The
    /// M3a behaviour, and still the answer for every game started from Steam,
    /// the Epic launcher, Galaxy or a desktop shortcut.
    /// </summary>
    public const string Inferred = "inferred";
}
