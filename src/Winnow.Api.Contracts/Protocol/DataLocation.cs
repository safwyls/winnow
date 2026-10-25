namespace Winnow.App.Services;

/// <summary>What the one-time move out of <c>%LOCALAPPDATA%\Hoard</c> did.</summary>
public enum DataMigrationOutcome
{
    /// <summary>Nothing to do: there is no legacy directory to move.</summary>
    None,

    /// <summary>The legacy directory was renamed onto the new path, whole.</summary>
    Moved,

    /// <summary>The rename was refused, so the tree was COPIED — staged beside the
    /// new path, checked, and only then renamed into place. The original is still
    /// on disk, untouched, and is now a backup.</summary>
    Copied,

    /// <summary>Both directories exist and the new one holds a database that
    /// opens. It wins and the legacy one is left exactly as it was — nothing is
    /// merged.</summary>
    BothPresent,

    /// <summary>Both directories exist, but the new one holds nothing that opens
    /// as a database while the legacy one still holds the library. Nothing was
    /// merged, moved or deleted; this run reads the legacy directory in place.</summary>
    LegacyPreferred,

    /// <summary>Something else has the legacy database open. Nothing was
    /// touched; this run reads the legacy directory in place.</summary>
    SourceBusy,

    /// <summary>Neither the move nor the copy worked. Nothing was left
    /// half-done; this run reads the legacy directory in place.</summary>
    Failed,

    /// <summary>An explicit <c>--data-dir</c> override was supplied. The
    /// legacy directory was never read, moved or copied; this run uses
    /// only the path the caller chose.</summary>
    Overridden,
}

/// <summary>The <c>--data-dir</c> override points at a path that cannot
/// be used as a data directory. Thrown rather than falling back, because
/// a silent return to the real library is the failure the flag exists
/// to prevent.</summary>
public sealed class DataDirectoryOverrideException : Exception
{
    public DataDirectoryOverrideException(string message)
        : base(message)
    {
    }

    public DataDirectoryOverrideException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>Where this run keeps its data, and how it got there.</summary>
/// <param name="Root">The directory holding the database, covers, themes and
/// the WebView2 profile.</param>
/// <param name="DatabasePath">The SQLite file inside <paramref name="Root"/>.
/// Named <c>hoard.db</c> only on the fallback paths, where the legacy directory
/// is being read where it lies.</param>
/// <param name="Outcome">What the backend data-location resolver did.</param>
public sealed record DataLocation(string Root, string DatabasePath, DataMigrationOutcome Outcome);
