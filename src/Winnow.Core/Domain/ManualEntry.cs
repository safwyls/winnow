namespace Winnow.Core.Domain;

/// <summary>
/// Everything the add-game form collects before a hand-added entry is
/// created. Validated by the repository; the domain type carries the
/// raw input.
/// </summary>
public sealed record ManualGameDraft
{
    /// <summary>The user-typed title. Required and trimmed before storage.</summary>
    public required string Title { get; init; }

    /// <summary>Optional release year, stored on the work.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary>Optional free-text platform label (e.g. "PC", "Switch").</summary>
    public string? PlatformLabel { get; init; }

    /// <summary>
    /// Optional path to the game's executable. Stored as evidence and used
    /// by <see cref="EffectiveInstallPath"/> to derive an install directory.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Optional explicit install path. When set, takes precedence over the
    /// directory inferred from <see cref="ExecutablePath"/>.
    /// </summary>
    public string? InstallPath { get; init; }

    /// <summary>Optional IGDB id. Checked for uniqueness before the entry is created.</summary>
    public long? IgdbId { get; init; }

    /// <summary>Optional Steam appid. Checked for uniqueness before the entry is created.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>The revision loaded by the edit form; null when creating or using a current-state command.</summary>
    public long? ExpectedIgdbMappingRevision { get; init; }

    /// <summary>The title after whitespace trimming.</summary>
    public string TrimmedTitle => Title.Trim();

    /// <summary>
    /// Prefers an explicit <see cref="InstallPath"/> and otherwise takes the
    /// executable's directory. Pure string work, no disk access. Null when
    /// neither field is set.
    /// </summary>
    public string? EffectiveInstallPath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(InstallPath))
            {
                return InstallPath.Trim();
            }

            if (string.IsNullOrWhiteSpace(ExecutablePath))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(ExecutablePath.Trim());
            return string.IsNullOrWhiteSpace(directory) ? null : directory;
        }
    }

    /// <summary>True when <see cref="EffectiveInstallPath"/> is non-null.</summary>
    public bool IsInstalled => EffectiveInstallPath is not null;
}

/// <summary>
/// A hand-added game as stored: the ownership's <c>manual_entries</c> row
/// joined to its work, release and ownership. The presence of this row is
/// the origin marker that distinguishes a manual entry from everything
/// ingest writes.
/// </summary>
public sealed record ManualEntry
{
    /// <summary>The ownership id, which is also the <c>manual_entries</c> primary key.</summary>
    public required long OwnershipId { get; init; }

    /// <summary>The release this ownership hangs off.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>The work the release belongs to.</summary>
    public required long WorkId { get; init; }

    /// <summary>The standing IGDB mapping displayed by the edit form.</summary>
    public long? IgdbId { get; init; }

    /// <summary>The manual Steam assertion, or an existing legacy identifier until explicitly resolved.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>Mapping revision loaded with this entry.</summary>
    public long IgdbMappingRevision { get; init; }

    /// <summary>The work's display title.</summary>
    public required string Title { get; init; }

    /// <summary>Release year, if the user supplied one.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary>Free-text platform label, if the user supplied one.</summary>
    public string? PlatformLabel { get; init; }

    /// <summary>Path to the game's executable, if the user supplied one.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Install path stored on the ownership, derived from the draft at creation.</summary>
    public string? InstallPath { get; init; }

    /// <summary>When the entry was first created (UTC).</summary>
    public required DateTime AddedAt { get; init; }

    /// <summary>When the entry was last edited (UTC). Equal to <see cref="AddedAt"/> until the first update.</summary>
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Thrown when a hand-added game's IGDB id or Steam appid already belongs to
/// another work in the library. <see cref="Field"/> names which identifier
/// conflicted, so the add form can point the user at the field to fix.
/// </summary>
public sealed class ManualEntryConflictException : InvalidOperationException
{
    public ManualEntryConflictException(string field, string message)
        : base(message) => Field = field;

    /// <summary>
    /// Which draft field conflicted: <c>IgdbId</c> or <c>SteamAppId</c>.
    /// </summary>
    public string Field { get; }

    /// <summary>Distinguishes another game's identifier from a correction that cannot safely retract old evidence.</summary>
    public ManualEntryConflictReason Reason { get; init; } = ManualEntryConflictReason.ClaimedByAnotherGame;
}

public enum ManualEntryConflictReason
{
    ClaimedByAnotherGame,
    LegacyIdentifierHistory,
    StorefrontObservation,
    MappingChanged,
}
