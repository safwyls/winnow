namespace Hoard.Core.Ingest;

/// <summary>
/// The normalised record every <c>Ingest.*</c> module emits (§5.1).
/// Ingest modules read a source and produce these; they must never write to
/// <c>works</c>/<c>releases</c> directly. The resolver (<c>Resolve.*</c>) maps
/// candidates onto Work/Release/Ownership, queueing ambiguous merges.
/// </summary>
/// <param name="Provider">Source platform key — see <see cref="Domain.ExternalIdProviders"/> (e.g. "steam").</param>
/// <param name="ProviderId">Platform-native id (Steam appid, GOG product id, Epic catalog item id).</param>
/// <param name="Title">
/// Raw title as the source reports it. Not normalised; the resolver owns normalisation.
/// <b>Null means the source knows the app exists but has no local title for it</b> — e.g. a
/// Steam appid that appears only in localconfig.vdf playtime, with no installed appmanifest to
/// name it. Such a candidate is <i>provisional</i>: the resolver gives it a placeholder name
/// flagged <c>name_is_provisional</c>, awaiting enrichment (or a later sync that carries a real
/// title). A real title must never be replaced by a provisional one.
/// </param>
/// <param name="AccountRef">Opaque reference to the source account (e.g. Steam3 id), if known.</param>
/// <param name="InstallPath">
/// Local install directory, if installed. Meaningful only when <paramref name="Installed"/>
/// is non-null: a source with no opinion on install state has no opinion on the path either,
/// and the two always move together (see <paramref name="Installed"/>).
/// </param>
/// <param name="Installed">
/// Whether the source reports the release as currently installed — <b>three-valued on
/// purpose</b>.
/// <list type="bullet">
/// <item><c>true</c>/<c>false</c>: the source inspected install state and this is its
/// answer. It overwrites what is stored, <c>false</c> included — uninstalling is a real
/// event and has to show.</item>
/// <item><c>null</c>: the source cannot know. Not "not installed". A null leaves both
/// <c>installed</c> and <c>install_path</c> exactly as they are.</item>
/// </list>
/// <para>Collapsing null into false is the bug this field exists to prevent: the Steam Web
/// API (§4.2) knows nothing about the local disk, so when its candidates were resolved
/// after the local scan's they cleared every install flag the appmanifests had just set,
/// and the "Installed" filter matched nothing on a library with games on disk. Which source
/// wins must follow from which source <i>knows</i>, never from which one happened to be
/// resolved last.</para>
/// </param>
/// <param name="PlaytimeMinutes">Cumulative playtime, if the source exposes it.</param>
/// <param name="LastPlayedAt">Last-played timestamp (UTC), if the source exposes it.</param>
/// <param name="AcquiredAt">Acquisition timestamp (UTC), if the source exposes it.</param>
/// <param name="Source">Which reader produced this (e.g. "steam_local", "gdpr_export") — kept for provenance.</param>
/// <param name="ObservedAt">When the reader observed this state (UTC).</param>
public sealed record CandidateOwnership(
    string Provider,
    string ProviderId,
    string? Title,
    string? AccountRef,
    string? InstallPath,
    bool? Installed,
    long? PlaytimeMinutes,
    DateTime? LastPlayedAt,
    DateTime? AcquiredAt,
    string Source,
    DateTime ObservedAt);
