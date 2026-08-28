using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>What the one-time move out of <c>%LOCALAPPDATA%\Hoard</c> did.</summary>
public enum DataMigrationOutcome
{
    /// <summary>Nothing to do: there is no legacy directory to move.</summary>
    None,

    /// <summary>The legacy directory was renamed onto the new path, whole.</summary>
    Moved,

    /// <summary>The rename was refused, so the tree was COPIED. The original is
    /// still on disk, untouched, and is now a backup.</summary>
    Copied,

    /// <summary>Both directories exist. The new one wins and the legacy one is
    /// left exactly as it was — nothing is merged.</summary>
    BothPresent,

    /// <summary>Something else has the legacy database open. Nothing was
    /// touched; this run reads the legacy directory in place.</summary>
    SourceBusy,

    /// <summary>Neither the move nor the copy worked. Nothing was left
    /// half-done; this run reads the legacy directory in place.</summary>
    Failed,
}

/// <summary>Where this run keeps its data, and how it got there.</summary>
/// <param name="Root">The directory holding the database, covers, themes and
/// the WebView2 profile.</param>
/// <param name="DatabasePath">The SQLite file inside <paramref name="Root"/>.
/// Named <c>hoard.db</c> only on the fallback paths, where the legacy directory
/// is being read where it lies.</param>
/// <param name="Outcome">What <see cref="WinnowDataLocation.Resolve(string, string, ILogger?)"/> did.</param>
public sealed record DataLocation(string Root, string DatabasePath, DataMigrationOutcome Outcome);

/// <summary>
/// The app's data directory, and the one-time migration of the library that
/// Hoard left at <c>%LOCALAPPDATA%\Hoard</c>.
///
/// <para><b>The rename is the dangerous part of the rename.</b> Changing the
/// path without moving what is under it does not produce an error — it produces
/// an empty database, silently, and a user who concludes their thousand-title
/// library is gone. So the path is not a constant that changed; it is resolved
/// once at startup, and resolving it is what performs the move.</para>
///
/// <para><b>Every failure lands on the old data, never on nothing.</b> If the
/// legacy database is open in another process, or the move and the copy both
/// fail, this returns the LEGACY directory and the app runs against the real
/// library exactly where it already is. That is the whole reason
/// <see cref="DataLocation"/> carries a path rather than this class exposing a
/// static one: a half-migrated library is worse than an unmigrated one, and an
/// empty one is worse than both.</para>
///
/// <para><b>Nothing here deletes the user's data.</b> The move is a directory
/// rename, which is the operating system relocating the tree in one verified
/// step. The copy fallback leaves the original in place forever. The only thing
/// ever deleted is a destination this class created moments earlier and could
/// not finish — reachable only after <c>Directory.Move</c> refused, which on
/// every platform means it copied nothing and the legacy tree is still whole.
/// Notably there is no rollback anywhere: putting a tree BACK is the only step
/// that could leave the library somewhere the caller is not about to look.</para>
/// </summary>
public static class WinnowDataLocation
{
    /// <summary>The folder under <c>%LOCALAPPDATA%</c>.</summary>
    public const string DirectoryName = "Winnow";

    /// <summary>The SQLite file inside it.</summary>
    public const string DatabaseFileName = "winnow.db";

    /// <summary>What the folder was called before the rename to Winnow.</summary>
    public const string LegacyDirectoryName = "Hoard";

    /// <summary>What the database was called before the rename to Winnow.</summary>
    public const string LegacyDatabaseFileName = "hoard.db";

    /// <summary>
    /// The database and both of SQLite's sidecars, in the order they are renamed.
    ///
    /// <para><b>The <c>-wal</c> is not optional baggage.</b> SQLite finds a
    /// write-ahead log by the database's file name, so a <c>winnow.db</c> beside
    /// an orphaned <c>hoard.db-wal</c> opens clean and silently drops every
    /// transaction that had not been checkpointed — which, after an unclean
    /// shutdown, is real sessions the user played.</para>
    /// </summary>
    private static readonly string[] DatabaseParts = ["", "-wal", "-shm"];

    /// <summary>The real paths under <c>%LOCALAPPDATA%</c>.</summary>
    public static DataLocation Resolve(ILogger? log = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Resolve(
            Path.Combine(local, DirectoryName),
            Path.Combine(local, LegacyDirectoryName),
            log);
    }

    /// <summary>
    /// Migrates if there is anything to migrate, then says which directory to
    /// use. Idempotent: on every run after the first, <paramref name="root"/>
    /// exists and the legacy path is never touched again.
    /// </summary>
    public static DataLocation Resolve(string root, string legacyRoot, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyRoot);

        var outcome = Migrate(root, legacyRoot, log);

        // The two failure outcomes are the point of this method. Both mean "the
        // library is still over there", so the app is pointed over there rather
        // than at an empty directory it would fill with a new database.
        var directory = outcome is DataMigrationOutcome.SourceBusy or DataMigrationOutcome.Failed
            ? legacyRoot
            : root;

        return new DataLocation(directory, DatabaseIn(directory), outcome);
    }

    /// <summary>
    /// The database inside a directory, found by looking rather than by
    /// assuming.
    ///
    /// <para>A run that moved the tree but could not rename the file inside it
    /// leaves a COMPLETE library under the new folder under the OLD file name.
    /// That is not a half-migration — everything is present and consistent, only
    /// the name is stale — and it must still open. Naming the file by rule
    /// rather than by inspection is precisely what would turn it into an empty
    /// database, so this looks; the next launch retries the rename.</para>
    /// </summary>
    private static string DatabaseIn(string directory)
    {
        var current = Path.Combine(directory, DatabaseFileName);
        var legacy = Path.Combine(directory, LegacyDatabaseFileName);

        return !File.Exists(current) && File.Exists(legacy) ? legacy : current;
    }

    private static DataMigrationOutcome Migrate(string root, string legacyRoot, ILogger? log)
    {
        var haveNew = Directory.Exists(root);
        var haveLegacy = Directory.Exists(legacyRoot);

        if (haveNew && haveLegacy)
        {
            // Deliberately not a merge. Two directories mean either a previous
            // copy-fallback (in which case the legacy one is a stale backup) or
            // a user who ran an old build after a new one — and in both cases
            // guessing which file is newer, per file, is how you produce a
            // library that is half of each. The new one is whole; it wins.
            log?.LogInformation(
                "Both {Root} and the legacy {Legacy} exist. Using {Root}; the legacy folder is left untouched and can be deleted once you are satisfied nothing is missing.",
                root, legacyRoot, root);
            TryRenameDatabase(root, log);
            return DataMigrationOutcome.BothPresent;
        }

        if (haveNew)
        {
            // The steady state, and the retry: if a previous run moved the tree
            // but could not rename the file inside it, this is where that gets
            // finished. Costs two File.Exists calls on every other launch.
            TryRenameDatabase(root, log);
            return DataMigrationOutcome.None;
        }

        if (!haveLegacy)
        {
            return DataMigrationOutcome.None;
        }

        if (IsBusy(legacyRoot))
        {
            // The app is already running, or a shell tool has the file open.
            // Moving half a tree out from under a live SQLite connection is the
            // one outcome worth failing loudly to avoid.
            log?.LogWarning(
                "The legacy database in {Legacy} is open in another process, so it was not moved to {Root}. Nothing was changed and this run is reading {Legacy} in place. Close the other copy and start again to complete the move.",
                legacyRoot, root, legacyRoot);
            return DataMigrationOutcome.SourceBusy;
        }

        try
        {
            // One rename for the database, the covers, the themes and the
            // WebView2 profile together. Same volume by construction — both
            // paths are under %LOCALAPPDATA% — so this is a directory-entry
            // rewrite, not a copy, and it either happened or it did not.
            Directory.Move(legacyRoot, root);

            // Deliberately NOT rolled back if this fails. Moving the tree back
            // is the only step in this method that could put the library
            // somewhere the caller is not about to look — and it is unnecessary,
            // because a moved tree whose database still has the old name is a
            // whole library, not a half-migrated one. DatabaseIn finds it, and
            // the next launch retries the rename.
            TryRenameDatabase(root, log);

            log?.LogInformation(
                "Moved your library from {Legacy} to {Root}, and renamed {LegacyDb} to {Db}. This happens once.",
                legacyRoot, root, LegacyDatabaseFileName, DatabaseFileName);
            return DataMigrationOutcome.Moved;
        }
        catch (Exception moveFailed) when (moveFailed is IOException or UnauthorizedAccessException)
        {
            log?.LogWarning(
                moveFailed,
                "Could not rename {Legacy} to {Root}; falling back to copying it.",
                legacyRoot, root);
        }

        try
        {
            CopyTree(legacyRoot, root);
            TryRenameDatabase(root, log);
            log?.LogInformation(
                "Copied your library from {Legacy} to {Root}. The original was NOT deleted — check {Root} looks right, then remove {Legacy} yourself.",
                legacyRoot, root, root, legacyRoot);
            return DataMigrationOutcome.Copied;
        }
        catch (Exception copyFailed)
        {
            log?.LogError(
                copyFailed,
                "Could not move or copy {Legacy} to {Root}. Your data has NOT been touched and this run is reading {Legacy} in place.",
                legacyRoot, root, legacyRoot);

            // Only ever our own partial work: this branch is unreachable unless
            // root did not exist when Migrate started, so everything under it
            // was written by the CopyTree call above.
            TryRemove(root, log);
            return DataMigrationOutcome.Failed;
        }
    }

    /// <summary>
    /// Whether anything holds the legacy database or a sidecar open.
    ///
    /// <para>An exclusive open is the only honest test — <c>File.Exists</c> and
    /// a lock file both answer a different question. A directory with no
    /// database in it (covers alone, say) is not busy.</para>
    /// </summary>
    private static bool IsBusy(string legacyRoot)
    {
        var database = Path.Combine(legacyRoot, LegacyDatabaseFileName);
        foreach (var part in DatabaseParts)
        {
            var path = database + part;
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var probe = new FileStream(
                    path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renames the database if it still needs it, and treats a failure as
    /// something to say rather than something to unwind — see the call site in
    /// <c>Migrate</c> for why a stale file name is not a broken library.
    /// </summary>
    private static void TryRenameDatabase(string root, ILogger? log)
    {
        if (!File.Exists(Path.Combine(root, LegacyDatabaseFileName)))
        {
            return;
        }

        try
        {
            RenameDatabase(root);
        }
        catch (Exception renameFailed)
            when (renameFailed is IOException or UnauthorizedAccessException)
        {
            log?.LogWarning(
                renameFailed,
                "Your library is at {Root}, but the database there is still called {LegacyDb}. Nothing is lost and it will be opened as it is; the rename is retried next launch.",
                root, LegacyDatabaseFileName);
        }
    }

    /// <summary>
    /// <c>hoard.db</c> and its sidecars become <c>winnow.db</c> and its
    /// sidecars, all or none. A partial rename is undone before the failure is
    /// allowed to propagate.
    /// </summary>
    private static void RenameDatabase(string root)
    {
        var from = Path.Combine(root, LegacyDatabaseFileName);
        var to = Path.Combine(root, DatabaseFileName);
        var done = new List<(string Source, string Destination)>();

        try
        {
            foreach (var part in DatabaseParts)
            {
                var source = from + part;
                var destination = to + part;
                if (!File.Exists(source) || File.Exists(destination))
                {
                    continue;
                }

                File.Move(source, destination);
                done.Add((source, destination));
            }
        }
        catch
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.Move(done[i].Destination, done[i].Source);
                }
                catch (IOException)
                {
                    // Best effort. The caller is about to put the whole
                    // directory back under its old name regardless.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            throw;
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryRemove(string directory, ILogger? log)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception cleanupFailed) when (cleanupFailed is IOException or UnauthorizedAccessException)
        {
            log?.LogWarning(
                cleanupFailed,
                "Left an incomplete copy at {Root}. Delete it by hand before starting again.",
                directory);
        }
    }
}
