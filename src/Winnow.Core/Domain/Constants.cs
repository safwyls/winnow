namespace Winnow.Core.Domain;

/// <summary>Valid <see cref="ExternalId.Provider"/> values (CHECK-constrained in the schema).</summary>
public static class ExternalIdProviders
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
    public const string Igdb = "igdb";

    /// <summary>
    /// Store-ingest providers (all except <see cref="Igdb"/>). Sweeps over the
    /// library should iterate this so no store is accidentally skipped.
    /// </summary>
    public static readonly IReadOnlyList<string> Stores = [Steam, Gog, Epic];
}

/// <summary>
/// How far two sources' playtime figures may disagree before the difference is
/// treated as play rather than as noise.
///
/// <para>Lives in Core because two layers now enforce the same rule and a second
/// literal <c>1</c> is how they would drift apart: <c>ExternalIdResolver</c>
/// applies it to the ownership-level series, and
/// <c>OwnershipAccountRepository</c> applies it to the per-account rows. If those
/// two ever used different bands, a library filtered to one account would report
/// a minute more than the same library unfiltered, for exactly the ownerships the
/// band was introduced to settle.</para>
/// </summary>
public static class PlaytimeTolerance
{
    /// <summary>
    /// Maximum disagreement, in minutes, absorbed as cross-source noise.
    /// Verified on the live database: <c>localconfig.vdf</c> reports 280 minutes
    /// for Portal (appid 400) while <c>GetOwnedGames</c> reports 279; the same
    /// one-minute gap appears on Arma 2 (3 vs 2) and Arma 2 Operation Arrowhead
    /// (154 vs 153). A move of one minute or less is disagreement; two minutes
    /// or more is play.
    /// </summary>
    public const long Minutes = 1;
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
    /// <summary>An open question. The grouped review queue reads these.</summary>
    public const string Pending = "pending";

    /// <summary>
    /// The only terminal status. 'confirmed' and 'undone' were dropped by
    /// migration 0019 with the destructive merge. A pair is answered
    /// affirmatively if and only if a live identity link exists between its
    /// two resolved works, so the affirmative answer has exactly one home
    /// and is retractable. 'undone' existed only to stop a re-merge loop
    /// under a model where nothing could be retracted.
    /// </summary>
    public const string Rejected = "rejected";

    /// <summary>
    /// Statuses that are an answer rather than a question.
    /// <c>SoftMatchResolver</c> leaves these alone.
    /// </summary>
    public static readonly IReadOnlyList<string> Terminal = [Rejected];
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
/// Valid <see cref="Session.AttributedBy"/> values (CHECK-constrained in the schema,
/// nullable because sessions recorded before M3b predate the column).
/// Stored because it cannot be reconstructed from a finished session.
/// </summary>
public static class SessionAttributions
{
    /// <summary>Winnow fired the launch; ownership is exact, not path-inferred.</summary>
    public const string Launch = "launch";

    /// <summary>Inferred from the executable's path or name matching an ownership.</summary>
    public const string Inferred = "inferred";
}
