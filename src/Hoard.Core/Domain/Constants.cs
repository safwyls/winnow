namespace Hoard.Core.Domain;

/// <summary>Valid <see cref="ExternalId.Provider"/> values (CHECK-constrained in the schema).</summary>
public static class ExternalIdProviders
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
    public const string Igdb = "igdb";

    /// <summary>
    /// The providers a store ingest writes — every provider except
    /// <see cref="Igdb"/>, which is Hoard's own canonical identity rather than
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
