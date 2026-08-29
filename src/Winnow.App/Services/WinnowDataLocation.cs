using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Winnow.Data;

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
/// Hoard left at <c>%LOCALAPPDATA%\Hoard</c>. Every failure lands on the old
/// data, never on nothing.
///
/// <para><b>Three rules hold this together, and each one is a failure that has
/// been reasoned about rather than a precaution:</b></para>
///
/// <para><b>1. A directory is adopted for what it contains, not for its
/// name.</b> When both directories exist the new one only wins if it holds a
/// database SQLite will open — otherwise the whole legacy library is still the
/// real one, and pointing at the new path would open an empty database beside a
/// thousand games.</para>
///
/// <para><b>2. A copy becomes the library by being renamed into place,
/// never by being written into place.</b> The copy lands in a uniquely named
/// staging sibling, is checked file by file against its source, and is promoted
/// with a single <see cref="Directory.Move(string, string)"/>. So a copy that
/// dies halfway — power, disk, a killed process — leaves a staging directory
/// nobody looks at, not a half-populated data directory the next launch would
/// adopt. Cleanup failing no longer matters, which is what makes the failure
/// path safe rather than merely tidy.</para>
///
/// <para><b>3. A database and its <c>-wal</c>/<c>-shm</c> sidecars are one
/// declared set.</b> They are renamed together or not at all, and only into a
/// directory where no file of the destination set exists. A <c>hoard.db-wal</c>
/// renamed to <c>winnow.db-wal</c> beside a different <c>winnow.db</c> is a
/// write-ahead log describing pages of another database: SQLite either refuses
/// the file or applies it, and the second one is silent corruption.</para>
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

    /// <summary>The database and both of SQLite's sidecars, in rename order.</summary>
    private static readonly string[] DatabaseParts = ["", "-wal", "-shm"];

    /// <summary>
    /// What separates the data directory's name from the unique tail of a
    /// staging directory: <c>Winnow.staging-1f3c…</c>, a sibling of
    /// <c>Winnow</c> so that promoting it is a rename on one volume.
    /// </summary>
    private const string StagingMarker = ".staging-";

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

        SweepAbandonedStaging(root, log);

        var outcome = Migrate(root, legacyRoot, log);

        // The three "the library is still over there" outcomes are the point of
        // this method. All of them mean the app is pointed at the old directory
        // rather than at one it would fill with a new, empty database.
        var directory = outcome is DataMigrationOutcome.SourceBusy
            or DataMigrationOutcome.Failed
            or DataMigrationOutcome.LegacyPreferred
            ? legacyRoot
            : root;

        return new DataLocation(directory, DatabaseIn(directory), outcome);
    }

    /// <summary>
    /// The database inside a directory, found by looking rather than assuming.
    /// Falls back to the legacy file name if the rename has not completed yet.
    ///
    /// <para>When both names are present — the one shape where the answer is a
    /// judgement rather than a lookup — the file that opens wins. Renaming is
    /// not an option there (rule 3), so opening the one that works in place is
    /// the whole of the remedy.</para>
    /// </summary>
    private static string DatabaseIn(string directory)
    {
        var current = Path.Combine(directory, DatabaseFileName);
        var legacy = Path.Combine(directory, LegacyDatabaseFileName);

        if (!File.Exists(current))
        {
            return File.Exists(legacy) ? legacy : current;
        }

        if (!File.Exists(legacy))
        {
            return current;
        }

        if (SqliteDatabaseCheck.Inspect(current).IsUsable)
        {
            return current;
        }

        // Only now is it worth the second open: the new name is there and does
        // not work. If the old name does not work either, the new name is still
        // the answer — a database has to be created somewhere, and it is not
        // going to be under a name the rename is trying to retire.
        return SqliteDatabaseCheck.Inspect(legacy).IsUsable ? legacy : current;
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
            // library that is half of each.
            TryRenameDatabase(root, log);
            return Choose(root, legacyRoot, log);
        }

        if (haveNew)
        {
            // The steady state, and the retry: if a previous run moved the tree
            // but could not rename the file inside it, this is where that gets
            // finished. Costs two File.Exists calls on every other launch — and
            // no database check, because with only one directory on disk there
            // is no second candidate a check could choose instead.
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

        return CopyThroughStaging(root, legacyRoot, log);
    }

    // ── Choosing between two directories ────────────────────────────────────

    /// <summary>
    /// Both directories exist. The new one wins unless it cannot produce a
    /// database that opens while the legacy one can — the case that used to be
    /// decided by the folder's name alone, and the case where deciding by name
    /// silently retires a complete library in favour of a failed copy.
    /// </summary>
    private static DataMigrationOutcome Choose(string root, string legacyRoot, ILogger? log)
    {
        var current = InspectDirectory(root);
        if (current.IsUsable)
        {
            log?.LogInformation(
                "Both {Root} and the legacy {Legacy} exist. Using {Root}; the legacy folder is left untouched and can be deleted once you are satisfied nothing is missing.",
                root, legacyRoot, root);
            return DataMigrationOutcome.BothPresent;
        }

        var legacy = InspectDirectory(legacyRoot);
        if (legacy.IsUsable)
        {
            log?.LogWarning(
                "{Root} exists but holds no database that opens ({Health}: {Detail}), while the legacy {Legacy} still holds your library. Nothing was merged, moved or deleted — this run reads {Legacy} in place. Move {Root} aside and start again to retry the migration.",
                root, current.Health, current.Detail, legacyRoot, legacyRoot, root);
            return DataMigrationOutcome.LegacyPreferred;
        }

        // Neither directory has anything to protect, so there is nothing to
        // choose between and the new path is where a new database belongs.
        log?.LogInformation(
            "Both {Root} and the legacy {Legacy} exist and neither holds a database that opens. Using {Root}; the legacy folder is left untouched.",
            root, legacyRoot, root);
        return DataMigrationOutcome.BothPresent;
    }

    /// <summary>
    /// The best database a directory can offer under either name. The new name
    /// is preferred, but only while it works.
    /// </summary>
    private static DatabaseCheck InspectDirectory(string directory)
    {
        var current = SqliteDatabaseCheck.Inspect(Path.Combine(directory, DatabaseFileName));
        if (current.IsUsable)
        {
            return current;
        }

        var legacy = SqliteDatabaseCheck.Inspect(Path.Combine(directory, LegacyDatabaseFileName));
        return legacy.IsUsable ? legacy : current;
    }

    // ── Copying: stage, check, promote ──────────────────────────────────────

    /// <summary>
    /// The fallback when the tree cannot simply be renamed — a different volume,
    /// or a directory entry something else holds.
    ///
    /// <para>Everything is written to a staging sibling and checked there. The
    /// destination gains its first byte at the promoting rename and not before,
    /// so there is no state of this method that leaves a partial library at
    /// <paramref name="root"/> for the next launch to adopt.</para>
    /// </summary>
    private static DataMigrationOutcome CopyThroughStaging(string root, string legacyRoot, ILogger? log)
    {
        var staging = root + StagingMarker + Guid.NewGuid().ToString("N");

        try
        {
            var source = InspectDirectory(legacyRoot);
            CopyTree(legacyRoot, staging, DatabaseSetIn(legacyRoot));
            Validate(legacyRoot, staging, source);

            // Done inside staging so the promoted directory arrives already
            // correct. Best effort, as everywhere else: a promoted tree whose
            // database still has the old name is whole, and the next launch
            // retries the rename.
            TryRenameDatabase(staging, log);

            // The promotion. One rename of one directory entry, on the volume
            // root lives on: either the library is there or it never was.
            Directory.Move(staging, root);

            log?.LogInformation(
                "Copied your library from {Legacy} to {Root}, checked it, and moved it into place. The original was NOT deleted — check {Root} looks right, then remove {Legacy} yourself.",
                legacyRoot, root, root, legacyRoot);
            return DataMigrationOutcome.Copied;
        }
        catch (Exception copyFailed)
        {
            log?.LogError(
                copyFailed,
                "Could not move or copy {Legacy} to {Root}. Your library has NOT been moved or deleted and this run is reading {Legacy} in place.",
                legacyRoot, root, legacyRoot);

            // Only ever our own staging directory, and only ever a tidy-up: if
            // this fails, the leftover is a sibling nothing reads, swept on the
            // next launch. root itself was never written to.
            TryRemove(staging, log);
            return DataMigrationOutcome.Failed;
        }
    }

    /// <summary>
    /// Proves the staged tree is the source tree before anything is allowed to
    /// promote it: every file present at the same length, the database set
    /// identical byte for byte, and — where the original opened — a copy that
    /// opens too.
    /// </summary>
    /// <remarks>
    /// The last check is conditional on the source, deliberately. Demanding a
    /// healthy staged database outright would mean a user whose library was
    /// already damaged could never migrate at all, and this method's promise is
    /// that the copy is no worse than the original, not that the original was
    /// good.
    /// </remarks>
    private static void Validate(string source, string staged, DatabaseCheck sourceCheck)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var copy = Path.Combine(staged, relative);

            if (!File.Exists(copy))
            {
                throw new IOException($"The copy in '{staged}' is missing '{relative}'.");
            }

            var original = new FileInfo(file).Length;
            var written = new FileInfo(copy).Length;
            if (original != written)
            {
                throw new IOException(
                    $"'{relative}' copied short: {written} bytes in '{staged}' against {original} in '{source}'.");
            }
        }

        // The database set gets the expensive check the covers do not. Length
        // equality is a fine proof that a JPEG arrived; it is not a proof that a
        // database did, and this is the one file in the tree that cannot be
        // re-downloaded.
        var databaseName = DatabaseNameIn(source);
        if (databaseName is not null)
        {
            foreach (var part in DatabaseParts)
            {
                var original = Path.Combine(source, databaseName + part);
                if (!File.Exists(original))
                {
                    continue;
                }

                var copy = Path.Combine(staged, databaseName + part);
                if (!Digest(original).SequenceEqual(Digest(copy)))
                {
                    throw new IOException(
                        $"'{databaseName + part}' does not match its original after copying.");
                }
            }
        }

        if (!sourceCheck.IsUsable)
        {
            return;
        }

        var stagedCheck = InspectDirectory(staged);
        if (!stagedCheck.IsUsable)
        {
            throw new IOException(
                $"The database copied into '{staged}' does not open as a Winnow database "
                + $"({stagedCheck.Health}: {stagedCheck.Detail}), though the original in "
                + $"'{source}' does.");
        }
    }

    private static byte[] Digest(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16);
        return SHA256.HashData(stream);
    }

    /// <summary>
    /// Deletes staging directories an earlier run left behind.
    ///
    /// <para>Safe by construction: a staging directory becomes the library only
    /// by being renamed into place, whole, so one that still exists under its
    /// staging name is work that was never adopted and never will be. Its source
    /// — the legacy tree — was not deleted either, so nothing here is the only
    /// copy of anything.</para>
    ///
    /// <para>Best effort: a directory another instance is filling right now has
    /// open handles and refuses to delete, which is the outcome we want anyway.
    /// </para>
    /// </summary>
    private static void SweepAbandonedStaging(string root, ILogger? log)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return;
        }

        var prefix = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) + StagingMarker;

        IEnumerable<string> abandoned;
        try
        {
            abandoned = Directory.EnumerateDirectories(parent, prefix + "*").ToList();
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in abandoned)
        {
            // Guard the enumeration's own pattern matching, which on Windows is
            // looser than it looks.
            if (!Path.GetFileName(directory).StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            log?.LogWarning(
                "Removing {Staging}, an incomplete copy left by an interrupted move. Nothing in it was ever used: a staged copy becomes your library only by being renamed into place, whole.",
                directory);
            TryRemove(directory, log);
        }
    }

    // ── The declared database set ───────────────────────────────────────────

    /// <summary>Whether anything holds the legacy database or a sidecar open.</summary>
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

    /// <summary>Which database file name a directory actually uses, if either.</summary>
    private static string? DatabaseNameIn(string directory)
    {
        if (File.Exists(Path.Combine(directory, DatabaseFileName)))
        {
            return DatabaseFileName;
        }

        return File.Exists(Path.Combine(directory, LegacyDatabaseFileName))
            ? LegacyDatabaseFileName
            : null;
    }

    /// <summary>The file names that make up the database set in a directory.</summary>
    private static HashSet<string> DatabaseSetIn(string directory)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = DatabaseNameIn(directory);
        if (name is null)
        {
            return set;
        }

        foreach (var part in DatabaseParts)
        {
            if (File.Exists(Path.Combine(directory, name + part)))
            {
                set.Add(name + part);
            }
        }

        return set;
    }

    /// <summary>
    /// Renames the database if it still needs it, and treats a failure as
    /// something to say rather than something to unwind — see the call site in
    /// <c>Migrate</c> for why a stale file name is not a broken library.
    /// </summary>
    private static void TryRenameDatabase(string root, ILogger? log)
    {
        try
        {
            RenameDatabase(root, log);
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
    /// sidecars, all or none — and only into a directory where no file of the
    /// destination set exists.
    ///
    /// <para>Three refusals, each of them a way to mix two databases:</para>
    /// <list type="bullet">
    /// <item>No <c>hoard.db</c>: an orphan <c>hoard.db-wal</c> is renamed by
    /// nothing. A write-ahead log without its database is not recoverable data,
    /// and moving one to the new name would put it beside a database it does not
    /// describe.</item>
    /// <item>Any destination file already present: the directory already holds a
    /// <c>winnow.db</c> set. Two sets in one directory are two databases, and
    /// completing one from the other is the corruption.</item>
    /// <item>Any single move failing: the ones already done are put back, so the
    /// caller sees the directory it started with.</item>
    /// </list>
    ///
    /// <para>The set is usually one file by the time it is renamed, because the
    /// checkpoint first folds the log into the database and lets SQLite delete
    /// the sidecars — which removes the crash window between the moves rather
    /// than narrowing it.</para>
    /// </summary>
    private static void RenameDatabase(string root, ILogger? log)
    {
        var from = Path.Combine(root, LegacyDatabaseFileName);
        var to = Path.Combine(root, DatabaseFileName);

        if (!File.Exists(from))
        {
            return;
        }

        var blocked = DatabaseParts
            .Select(part => to + part)
            .Where(File.Exists)
            .Select(Path.GetFileName)
            .ToList();

        if (blocked.Count > 0)
        {
            log?.LogWarning(
                "{Root} holds both {LegacyDb} and {Blocked}. Nothing was renamed: those are two separate databases, and completing one from the other's files would corrupt it. The one that opens is the one being used; move whichever you do not want out of the folder.",
                root, LegacyDatabaseFileName, string.Join(", ", blocked));
            return;
        }

        SqliteDatabaseCheck.TryCheckpoint(from);

        var done = new List<(string Source, string Destination)>();
        try
        {
            foreach (var part in DatabaseParts)
            {
                var source = from + part;
                if (!File.Exists(source))
                {
                    continue;
                }

                var destination = to + part;
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

    // ── Copying files ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies a tree, flushing the named files to the platter before closing
    /// them.
    /// </summary>
    /// <param name="flushToDisk">
    /// The database set. Not every file: <c>FlushFileBuffers</c> per file across
    /// a covers directory with thousands of JPEGs turns a one-time copy into a
    /// visibly slow one, and a cover that has to be fetched again is not the
    /// failure this class exists to prevent. There is no portable way to fsync
    /// the directory entries themselves from .NET, which is the remaining gap and
    /// the reason the promotion is a rename rather than a write.
    /// </param>
    private static void CopyTree(string source, string destination, IReadOnlySet<string> flushToDisk)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            CopyFile(file, Path.Combine(destination, name), flushToDisk.Contains(name));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)), flushToDisk);
        }
    }

    private static void CopyFile(string source, string destination, bool flushToDisk)
    {
        using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16);
        using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1 << 16);

        input.CopyTo(output);
        output.Flush(flushToDisk);
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
                "Left an incomplete copy at {Staging}. Nothing reads it and the next launch removes it; delete it by hand if you would rather have the space back now.",
                directory);
        }
    }
}
