namespace Hoard.Ingest.Gog;

/// <summary>
/// One owned GOG-native release as Galaxy's client database reports it
/// (docs/spikes/epic-gog-local-files.md section 13), for one Galaxy user.
/// </summary>
/// <param name="ReleaseKey">
/// Galaxy's key, e.g. <c>gog_1971477531</c>. Format is
/// <c>&lt;platform&gt;_&lt;externalId&gt;</c> and the schema enforces it.
/// </param>
/// <param name="ProductId">
/// The GOG product id — the release key's suffix. This is the external id: it is
/// byte-identical to the registry's <c>gameID</c>, to <c>goggame-&lt;id&gt;.info</c>'s
/// <c>gameId</c>, and to IGDB's <c>external_games</c> uid for source 5, so it hard-joins
/// with no transformation.
/// </param>
/// <param name="UserId">Galaxy user this row belongs to (<c>Users.id</c>).</param>
/// <param name="Title">
/// From the <c>title</c> GamePiece — <b>the canonical store title</b>, and the one
/// to display. Every install-side source carries the installer-locale title
/// instead (GWENT's registry name is Polish), so this is not interchangeable with
/// them.
/// </param>
/// <param name="IsDlc">From <c>ReleaseProperties.isDlc</c>, coalesced from null.</param>
/// <param name="IsVisibleInLibrary">From <c>ReleaseProperties.isVisibleInLibrary</c>, coalesced from null.</param>
/// <param name="PlaytimeMinutes">
/// <c>GameTimes.minutesInGame</c>, in <b>minutes</b>. Null when there is no row at
/// all; <c>0</c> when Galaxy has a row saying zero, which is a real answer and not
/// the same thing. Galaxy only accrues time for sessions it launched.
/// </param>
/// <param name="LastPlayedUtc">
/// <c>LastPlayedDates.lastPlayedDate</c>, parsed as UTC. Null when there is no
/// row, which is the ordinary state of a never-played game and never an error.
/// </param>
/// <param name="PurchasedAtUtc"><c>ProductPurchaseDates.purchaseDate</c>, parsed as UTC.</param>
/// <param name="AddedAtUtc"><c>ProductPurchaseDates.addedDate</c>, parsed as UTC.</param>
/// <param name="InstallationPath">
/// <c>InstalledBaseProducts.installationPath</c>, reached through
/// <c>ProductsToReleaseKeys.gogId</c>. Null means not installed.
/// </param>
/// <param name="InstalledAtUtc"><c>InstalledBaseProducts.installationDate</c>, parsed as UTC.</param>
/// <param name="BuildId"><c>InstalledBaseProducts.buildId</c>; matches the registry's <c>BUILDID</c>.</param>
public sealed record GogLibraryEntry(
    string ReleaseKey,
    string ProductId,
    long UserId,
    string? Title,
    bool IsDlc,
    bool IsVisibleInLibrary,
    long? PlaytimeMinutes,
    DateTime? LastPlayedUtc,
    DateTime? PurchasedAtUtc,
    DateTime? AddedAtUtc,
    string? InstallationPath,
    DateTime? InstalledAtUtc,
    long? BuildId)
{
    /// <summary>
    /// Install state as the database reports it. <b>Never gate playtime on this:</b>
    /// playtime survives uninstall — The Witcher 3 shows 50 minutes and a 2018
    /// last-played while not installed.
    /// </summary>
    public bool IsInstalled => !string.IsNullOrWhiteSpace(InstallationPath);
}
