namespace Winnow.Ingest.Gog;

/// <summary>
/// A <c>goggame-&lt;gameId&gt;.info</c> file from a game's install directory
/// (docs/spikes/epic-gog-local-files.md section 16). UTF-8, no BOM, LF, 4-space
/// indent. Written by every GOG installer, Galaxy or not, so it is the second
/// half of the Galaxy-less path — and the only source that can tell DLC from a
/// base game without Galaxy.
/// </summary>
/// <param name="GameId">GOG product id. Identical to the registry's <c>gameID</c> and to the <c>gog_</c> release key suffix.</param>
/// <param name="RootGameId">
/// The base game's id. <b><c>GameId != RootGameId</c> marks a DLC</b> — the only
/// DLC discriminator available without Galaxy.
/// </param>
/// <param name="Name">
/// The <b>installer-locale</b> title, not the store title. GWENT's is Polish
/// (<c>GWINT: Wiedźmińska Gra Karciana</c>) on an English install with
/// <c>installer_language = english</c> — it is what the publisher stamped into
/// that installer build. Low-confidence: prefer Galaxy's <c>title</c> GamePiece
/// wherever it exists, and never let this string reach a fuzzy title matcher.
/// </param>
/// <param name="BuildId">Matches the registry's <c>BUILDID</c> and Galaxy's <c>InstalledBaseProducts.buildId</c>.</param>
/// <param name="PrimaryPlayTaskPath">
/// <c>playTasks[]</c>'s primary <c>path</c>, <b>relative to the install
/// directory</b>. Raw material for §5.2's process monitor.
/// </param>
/// <param name="FilePath">Absolute path of the file this was read from.</param>
public sealed record GogGameInfo(
    string GameId,
    string RootGameId,
    string? Name,
    string? BuildId,
    string? PrimaryPlayTaskPath,
    string FilePath)
{
    /// <summary>
    /// True when this describes DLC rather than a base game. Treats an absent
    /// <c>rootGameId</c> as "base game", because the field is only meaningfully
    /// different on a child.
    /// </summary>
    public bool IsDlc
        => RootGameId.Length > 0 && !string.Equals(GameId, RootGameId, StringComparison.Ordinal);
}
