namespace Hoard.Core.Domain;

/// <summary>Valid <see cref="ExternalId.Provider"/> values (CHECK-constrained in the schema).</summary>
public static class ExternalIdProviders
{
    public const string Steam = "steam";
    public const string Gog = "gog";
    public const string Epic = "epic";
    public const string Igdb = "igdb";
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
