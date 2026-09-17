using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Winnow.Update;

public static class PortableUpdateEngine
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Workspace(string installation) => Path.Combine(Path.GetDirectoryName(Full(installation)) ?? throw new IOException("Cannot update a volume root."), "." + Path.GetFileName(Full(installation)) + ".winnow-update");
    public static string GetJournalPath(string installationDirectory) => Path.Combine(Workspace(installationDirectory), "journal.json");
    private static string Root(string journal) => Path.GetDirectoryName(Path.GetFullPath(journal))!;
    private static bool Inside(string path, string root) => Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, Comparison);
    private static void NoLinks(string path)
    {
        for (string? current = Full(path); current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked installation and data paths are not supported.");
    }
    public static IDisposable AcquireApplicationLease(string installationDirectory)
    {
        var path = Workspace(installationDirectory) + ".lock";
        NoLinks(path);
        if (!File.Exists(path)) { using var created = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite); }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    private static IDisposable ExclusiveLease(string installation)
    {
        var path = Workspace(installation) + ".lock"; NoLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static IDisposable OperationLease(string installation)
    {
        var path = Workspace(installation) + ".operation.lock"; NoLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public static string Stage(string archivePath, string expectedSha256, string version, string runtime, string installationDirectory, string dataDirectory, string executableName, string[] restartArguments, Func<string, long>? availableSpace = null, string? transactionId = null)
    {
        installationDirectory = Full(installationDirectory); dataDirectory = Full(dataDirectory);
        using var operation = OperationLease(installationDirectory);
        if (transactionId is not null && !Guid.TryParseExact(transactionId, "N", out _)) throw new ArgumentException("Transaction identity must be a GUID in N format.", nameof(transactionId));
        if (ReleaseVersion.Parse(version) is null || runtime is not ("win-x64" or "linux-x64")) throw new IOException("Unsupported release metadata.");
        NoLinks(installationDirectory); NoLinks(dataDirectory);
        if (!Directory.Exists(installationDirectory) || Path.GetFileName(executableName) != executableName || !File.Exists(Path.Combine(installationDirectory, executableName))) throw new IOException("The portable installation is not valid.");
        if (dataDirectory.Equals(installationDirectory, Comparison) || Inside(installationDirectory, dataDirectory)) throw new IOException("The selected data directory must not contain the installation.");
        if (restartArguments.Any(a => a is not ("--fullscreen" or "--no-sync"))) throw new IOException("Unsupported restart argument.");
        using (var installed = JsonDocument.Parse(File.ReadAllText(Path.Combine(installationDirectory, "release-info.json"))))
        {
            if (installed.RootElement.GetProperty("runtime").GetString() != runtime || installed.RootElement.TryGetProperty("package-managed", out _)) throw new IOException("This installation does not support portable replacement.");
        }
        if (File.Exists(Path.Combine(installationDirectory, "package-managed"))) throw new IOException("Package-managed installations must use their package manager.");
        if (Directory.EnumerateFiles(installationDirectory, "unins*.exe").Any()) throw new IOException("Registered Windows installations must use the installer update route.");
        if (!OperatingSystem.IsWindows() && (installationDirectory == "/opt" || installationDirectory == "/usr" || Inside(installationDirectory, "/opt") || Inside(installationDirectory, "/usr"))) throw new IOException("System installations must use their package manager.");
        using var archive = File.OpenRead(archivePath);
        if (!Convert.ToHexString(SHA256.HashData(archive)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The update archive digest does not match.");
        archive.Position = 0;
        var workspace = Workspace(installationDirectory);
        NoLinks(workspace);
        if (dataDirectory.Equals(workspace, Comparison) || Inside(dataDirectory, workspace)) throw new IOException("The data directory overlaps the update workspace.");
        if (Directory.Exists(workspace))
        {
            var existing = File.Exists(GetJournalPath(installationDirectory)) ? ReadJournal(GetJournalPath(installationDirectory)) : null;
            if (existing?.Phase == UpdatePhase.Staged) Directory.Delete(workspace, true);
            else
            {
                if (existing is not null && existing.Phase is not (UpdatePhase.Ready or UpdatePhase.Restored)) throw new IOException("A previous update needs recovery or cleanup first: " + workspace);
                DurableFiles.Move(workspace, workspace + ".retained-" + Guid.NewGuid().ToString("N"), directory: true);
            }
        }
        Directory.CreateDirectory(workspace);
        try
        {
            var extracted = Path.Combine(workspace, "extracted"); Directory.CreateDirectory(extracted); Extract(archive, archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), extracted, (availableSpace ?? AvailableSpace)(workspace));
            archive.Position = 0;
            if (!Convert.ToHexString(SHA256.HashData(archive)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The update archive changed while staging.");
            var payload = extracted;
            if (!File.Exists(Path.Combine(payload, "release-info.json")))
            {
                var children = Directory.GetDirectories(payload);
                if (children.Length != 1 || Directory.GetFiles(payload).Length != 0) throw new IOException("Archive layout is not supported.");
                payload = children[0];
            }
            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, "release-info.json"))))
                if (manifest.RootElement.GetProperty("version").GetString() != version || manifest.RootElement.GetProperty("runtime").GetString() != runtime) throw new IOException("Archive release metadata does not match the selected release.");
            if (!File.Exists(Path.Combine(payload, executableName))) throw new IOException("Archive is missing Winnow.");
            var stage = Path.Combine(workspace, "staged"); Directory.Move(payload, stage);
            if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
            if (Inside(dataDirectory, installationDirectory) && Path.Exists(Path.Combine(stage, Path.GetRelativePath(installationDirectory, dataDirectory)))) throw new IOException("The archive collides with the selected data directory.");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(stage, executableName), (UnixFileMode)493);
            var path = GetJournalPath(installationDirectory);
            Write(path, new UpdateJournal { TransactionId = transactionId ?? Guid.NewGuid().ToString("N"), InstallationDirectory = installationDirectory, DataDirectory = dataDirectory, ExecutableName = executableName, Version = version, Runtime = runtime, RestartArguments = restartArguments, Phase = UpdatePhase.Staged, PayloadHashes = HashPayload(stage) });
            return path;
        }
        catch { Directory.Delete(workspace, true); throw; }
    }
    private static Dictionary<string, string> HashPayload(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            NoLinks(file); using var input = File.OpenRead(file); result.Add(Path.GetRelativePath(root, file), Convert.ToHexString(SHA256.HashData(input)));
        }
        return result;
    }

    private static long AvailableSpace(string path)
    {
        var full = Path.GetFullPath(path);
        var drive = DriveInfo.GetDrives()
            .Where(candidate => full.StartsWith(candidate.Name.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault() ?? new DriveInfo(Path.GetPathRoot(full)!);
        return drive.AvailableFreeSpace;
    }
    private static void RequireSpace(long available, long required)
    {
        if (available < checked(required + 64L * 1024 * 1024)) throw new IOException("There is not enough free space to safely update Winnow.");
    }
    private static void Extract(Stream archive, bool isZip, string destination, long available)
    {
        // Linux packages contain both the Winnow apphost and the winnow shell launcher.
        // Match the target platform's path semantics while retaining duplicate rejection.
        long total = 0; var names = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string Target(string name, long length)
        {
            name = name.Replace('\\', '/');
            if (name.StartsWith('/') || name.Split('/').Any(p => p is ".." or "." || p.Contains(':') || p.EndsWith(' ') || p.EndsWith('.')) || !names.Add(name.TrimEnd('/'))) throw new IOException("Unsafe or duplicate archive entry.");
            var target = Path.GetFullPath(Path.Combine(destination, name));
            if (!Inside(target, destination)) throw new IOException("Archive entry escapes staging.");
            total = checked(total + length); if (total > 8L * 1024 * 1024 * 1024) throw new IOException("Expanded update exceeds the staging limit.");
            RequireSpace(available, total);
            return target;
        }
        if (isZip)
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in zip.Entries)
            {
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Archive links are not supported.");
                var path = Target(entry.FullName, entry.Length);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var source = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew); source.CopyTo(output); output.Flush(true);
            }
        }
        else
        {
            using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true); using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Archive links and special entries are not supported.");
                var path = Target(entry.Name, entry.Length);
                if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = new FileStream(path, FileMode.CreateNew); entry.DataStream?.CopyTo(output); output.Flush(true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)511);
            }
        }
    }
    public static UpdateJournal ReadJournal(string journalPath)
    {
        NoLinks(journalPath);
        using var input = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var journal = JsonSerializer.Deserialize(input, UpdateJsonContext.Default.UpdateJournal) ?? throw new IOException("Invalid update journal.");
        if (!Path.GetFullPath(journalPath).Equals(GetJournalPath(journal.InstallationDirectory), Comparison)) throw new IOException("Update journal does not own this installation.");
        NoLinks(journal.InstallationDirectory); NoLinks(journal.DataDirectory);
        if (Path.GetFileName(journal.ExecutableName) != journal.ExecutableName || journal.RestartArguments.Any(a => a is not ("--fullscreen" or "--no-sync")) || journal.DataDirectory.Equals(journal.InstallationDirectory, Comparison) || Inside(journal.InstallationDirectory, journal.DataDirectory) || Inside(journal.DataDirectory, Root(journalPath)) || journal.DataDirectory.Equals(Root(journalPath), Comparison)) throw new IOException("Invalid update journal paths or arguments.");
        return journal;
    }
    private static void Write(string path, UpdateJournal journal)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(journal, UpdateJsonContext.Default.UpdateJournal);
        using (var output = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { output.Write(bytes); output.Flush(true); }
        // Windows metadata readers and scanners can briefly deny replacement,
        // even while the helper and app coordinate their own journal writes.
        // Retry only this atomic rename; never replay an update phase or remove
        // the old journal to make room. A persistent lock still fails startup.
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { DurableFiles.Move(path + ".tmp", path, replace: true); return; }
            catch (System.ComponentModel.Win32Exception error) when (OperatingSystem.IsWindows() &&
                error.NativeErrorCode is 5 or 32 or 33 && timer.Elapsed < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(25);
            }
        }
    }
    private static IDisposable StateLease(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path + ".state-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(2)) { Thread.Sleep(10); }
        }
    }
    private static void ValidateTransaction(UpdateJournal journal, string? expectedTransactionId)
    {
        if (expectedTransactionId is not null && journal.TransactionId != expectedTransactionId) throw new IOException("Another update replaced this staging operation. Download the update again.");
    }
    public static void DiscardStaged(string journalPath, string? expectedTransactionId = null)
    {
        var journal = ReadJournal(journalPath); using var operation = OperationLease(journal.InstallationDirectory);
        journal = ReadJournal(journalPath); ValidateTransaction(journal, expectedTransactionId);
        if (journal.Phase != UpdatePhase.Staged) throw new IOException("An applied update must be recovered explicitly.");
        Directory.Delete(Root(journalPath), true);
    }
    public static string PrepareHelper(string journalPath, string? expectedTransactionId = null)
    {
        var journal = ReadJournal(journalPath); using var operation = OperationLease(journal.InstallationDirectory);
        journal = ReadJournal(journalPath); ValidateTransaction(journal, expectedTransactionId);
        if (journal.Phase != UpdatePhase.Staged) throw new IOException("This update is no longer staged.");
        var target = Path.Combine(Root(journalPath), "helper");
        foreach (var marker in new[] { "helper-ready", "proceed", "cancel" }) File.Delete(Path.Combine(Root(journalPath), marker));
        if (Directory.Exists(target)) Directory.Delete(target, true);
        CopyDirectory(Path.Combine(journal.InstallationDirectory, "update-helper"), target);
        return Path.Combine(target, "Winnow.Update.Helper" + (OperatingSystem.IsWindows() ? ".exe" : ""));
    }
    private static void CopyDirectory(string source, string target)
    {
        NoLinks(source); Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) { NoLinks(file); var dest = Path.Combine(target, Path.GetFileName(file)); File.Copy(file, dest); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, File.GetUnixFileMode(file)); }
        foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    public static async Task ApplyAsync(string journalPath, int? parentPid = null, long? parentStartTicks = null, TimeSpan? readyTimeout = null, Func<string, long>? availableSpace = null, string? expectedTransactionId = null)
    {
        var initial = ReadJournal(journalPath); using var operation = OperationLease(initial.InstallationDirectory);
        initial = ReadJournal(journalPath); ValidateTransaction(initial, expectedTransactionId);
        if (parentPid is { } pid)
        {
            try
            {
                if (parentStartTicks is null or <= 0) throw new IOException("An exact parent process start time is required.");
                File.WriteAllText(Path.Combine(Root(journalPath), "helper-ready"), initial.TransactionId);
                var handshake = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(Root(journalPath), "proceed")) || File.ReadAllText(Path.Combine(Root(journalPath), "proceed")) != initial.TransactionId)
                {
                    if ((File.Exists(Path.Combine(Root(journalPath), "cancel")) && File.ReadAllText(Path.Combine(Root(journalPath), "cancel")) == initial.TransactionId) || handshake.Elapsed > TimeSpan.FromSeconds(20)) throw new IOException("Update handoff was cancelled.");
                    await Task.Delay(100);
                }
                try
                {
                    using var parent = Process.GetProcessById(pid);
                    if (parent.StartTime.ToUniversalTime().Ticks == parentStartTicks) { using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); await parent.WaitForExitAsync(timeout.Token); }
                }
                catch (ArgumentException) { }
            }
            catch (Exception error)
            {
                initial.Failure = error.Message;
                Write(journalPath, initial);
                throw;
            }
        }
        var journal = ReadJournal(journalPath);
        try
        {
            using (ExclusiveLease(journal.InstallationDirectory))
            {
                using var dataLease = AcquireDataLease(journal.DataDirectory);
                if (journal.Phase != UpdatePhase.Staged) throw new IOException("Update has already begun; use recovery.");
                var actualHashes = HashPayload(Path.Combine(Root(journalPath), "staged"));
                if (actualHashes.Count != journal.PayloadHashes.Count || actualHashes.Any(pair => !journal.PayloadHashes.TryGetValue(pair.Key, out var expected) || pair.Value != expected)) throw new IOException("Staged update files changed after verification.");
                var db = Path.Combine(journal.DataDirectory, "winnow.db"); journal.DatabaseExisted = File.Exists(db);
                RequireSpace((availableSpace ?? AvailableSpace)(Root(journalPath)), journal.DatabaseExisted ? new FileInfo(db).Length + (File.Exists(db + "-wal") ? new FileInfo(db + "-wal").Length : 0) : 0);
                if (journal.DatabaseExisted)
                {
                    using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                    using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root(journalPath), "before.db"), Pooling = false }.ToString());
                    source.Open(); backup.Open(); source.BackupDatabase(backup);
                    using var check = backup.CreateCommand(); check.CommandText = "PRAGMA integrity_check"; if ((string?)check.ExecuteScalar() != "ok") throw new IOException("Pre-update database backup failed verification.");
                }
                if (journal.DatabaseExisted) { using var flush = new FileStream(Path.Combine(Root(journalPath), "before.db"), FileMode.Open, FileAccess.ReadWrite); flush.Flush(true); }
                DurableFiles.FlushDirectory(Root(journalPath));
                journal.BackupCompleted = true;
                journal.Phase = UpdatePhase.BackupComplete; Write(journalPath, journal);
                journal.Phase = UpdatePhase.Replacing; Write(journalPath, journal);
                DurableFiles.Move(journal.InstallationDirectory, Path.Combine(Root(journalPath), "previous"), directory: true);
                DurableFiles.Move(Path.Combine(Root(journalPath), "staged"), journal.InstallationDirectory, directory: true);
                MoveData(journal, Path.Combine(Root(journalPath), "previous"), journal.InstallationDirectory);
                journal.Phase = UpdatePhase.Installed;
                // A competing launch can open the database once the installation lease is released.
                journal.MigrationMayHaveStarted = true; Write(journalPath, journal);
            }
            var start = new ProcessStartInfo(Path.Combine(journal.InstallationDirectory, journal.ExecutableName)) { UseShellExecute = false, WorkingDirectory = journal.InstallationDirectory };
            start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(journal.DataDirectory);
            foreach (var arg in journal.RestartArguments) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--update-journal"); start.ArgumentList.Add(Path.GetFullPath(journalPath));
            using var child = Process.Start(start) ?? throw new IOException("Could not start the updated application.");
            File.WriteAllText(Path.Combine(Root(journalPath), "child-process"), $"{child.Id}\n{child.StartTime.ToUniversalTime().Ticks}");
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < (readyTimeout ?? TimeSpan.FromMinutes(2)))
            {
                if (ReadJournal(journalPath).Phase == UpdatePhase.Ready) return;
                if (child.HasExited) throw new IOException($"Updated Winnow exited before ready (code {child.ExitCode}).");
                await Task.Delay(250);
            }
            throw new IOException("Updated Winnow did not report ready. Close it before recovery; reinstall the same or a newer version, or explicitly restore the paired backup.");
        }
        catch (Exception ex)
        {
            using var state = StateLease(journalPath);
            journal = ReadJournal(journalPath);
            if (journal.Phase != UpdatePhase.Ready) { journal.Phase = UpdatePhase.RecoveryRequired; journal.Failure = ex.Message; Write(journalPath, journal); }
            throw;
        }
    }
    private static IDisposable AcquireDataLease(string dataDirectory)
    {
        var normalized = Full(dataDirectory).ToUpperInvariant();
        var name = "Local\\Winnow.Data." + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        var mutex = new Mutex(false, name, out var created);
        if (!created) { mutex.Dispose(); throw new IOException("Another Winnow instance is using the selected data directory."); }
        return mutex;
    }
    private static void MoveData(UpdateJournal journal, string from, string to)
    {
        if (!Inside(journal.DataDirectory, journal.InstallationDirectory)) return;
        var relative = Path.GetRelativePath(journal.InstallationDirectory, journal.DataDirectory); var source = Path.Combine(from, relative); var target = Path.Combine(to, relative);
        if (!Directory.Exists(source)) return;
        if (Directory.Exists(target)) throw new IOException("Recovery found two selected data directories; neither was overwritten.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); DurableFiles.Move(source, target, directory: true);
    }
    public static void ValidateStartup(string journalPath, string installationDirectory, string dataDirectory)
    {
        using var state = StateLease(journalPath);
        var journal = ReadJournal(journalPath);
        if (!Full(installationDirectory).Equals(journal.InstallationDirectory, Comparison) || !Full(dataDirectory).Equals(journal.DataDirectory, Comparison)) throw new IOException("Update startup paths do not match the journal.");
        if (journal.Phase != UpdatePhase.Installed) throw new IOException("This update requires recovery: " + journalPath);
        journal.MigrationMayHaveStarted = true; journal.Phase = UpdatePhase.MigrationStarted; Write(journalPath, journal);
    }
    public static void MarkReady(string journalPath)
    {
        using var state = StateLease(journalPath);
        var journal = ReadJournal(journalPath);
        if (journal.Phase != UpdatePhase.MigrationStarted) throw new IOException("Update startup handshake is out of sequence.");
        journal.Phase = UpdatePhase.Ready; journal.Failure = null; Write(journalPath, journal);
    }
    public static void Recover(string journalPath, bool restoreBackup = false)
    {
        var journal = ReadJournal(journalPath); using var operation = OperationLease(journal.InstallationDirectory); using var lease = ExclusiveLease(journal.InstallationDirectory);
        journal = ReadJournal(journalPath);
        using var dataLease = AcquireDataLease(journal.DataDirectory);
        if (journal.Phase == UpdatePhase.Restored) return;
        if ((journal.MigrationMayHaveStarted || journal.DatabaseRestoreStarted) && !restoreBackup) throw new IOException("Database migration or paired restoration may have started. Reinstall the same or a newer version, or explicitly restore the paired database and binaries.");
        var root = Root(journalPath); var previous = Path.Combine(root, "previous");
        if (restoreBackup)
        {
            if (!journal.BackupCompleted || (!Directory.Exists(previous) && journal.Phase != UpdatePhase.Restoring)) throw new IOException("No complete paired backup is available; recovery did not change files.");
            if (journal.DatabaseExisted) ValidateDatabaseBackup(Path.Combine(root, "before.db"));
        }
        journal.DatabaseRestoreStarted |= restoreBackup;
        journal.Phase = UpdatePhase.Restoring; Write(journalPath, journal);
        if (Directory.Exists(previous))
        {
            if (Directory.Exists(journal.InstallationDirectory)) { MoveData(journal, journal.InstallationDirectory, previous); DurableFiles.Move(journal.InstallationDirectory, Path.Combine(root, "failed"), directory: true); }
            DurableFiles.Move(previous, journal.InstallationDirectory, directory: true);
        }
        if (restoreBackup)
        {
            var db = Path.Combine(journal.DataDirectory, "winnow.db"); Directory.CreateDirectory(journal.DataDirectory);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var evidence = Path.Combine(root, "post-upgrade.db" + suffix);
                if (File.Exists(db + suffix))
                {
                    if (!File.Exists(evidence)) CopyPersisted(db + suffix, evidence);
                    if (suffix.Length > 0 || !journal.DatabaseExisted) File.Delete(db + suffix);
                }
            }
            DurableFiles.FlushDirectory(journal.DataDirectory);
            if (journal.DatabaseExisted)
            {
                File.Copy(Path.Combine(root, "before.db"), db + ".restore", true);
                using (var flush = new FileStream(db + ".restore", FileMode.Open, FileAccess.ReadWrite)) flush.Flush(true);
                DurableFiles.Move(db + ".restore", db, replace: true);
            }
        }
        journal.Phase = UpdatePhase.Restored; Write(journalPath, journal);
    }
    private static void CopyPersisted(string source, string target)
    {
        // Data may live on another volume. Persist evidence before removing or replacing its source.
        File.Copy(source, target + ".saving", true);
        using (var output = new FileStream(target + ".saving", FileMode.Open, FileAccess.ReadWrite)) output.Flush(true);
        DurableFiles.Move(target + ".saving", target);
    }
    private static void ValidateDatabaseBackup(string path)
    {
        if (!File.Exists(path)) throw new IOException("Paired database backup is missing; recovery did not change files.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        if ((string?)command.ExecuteScalar() != "ok") throw new IOException("Paired database backup is corrupt; recovery did not change files.");
    }

    public static void Resume(string journalPath)
    {
        var journal = ReadJournal(journalPath); using var operation = OperationLease(journal.InstallationDirectory); using var lease = ExclusiveLease(journal.InstallationDirectory);
        journal = ReadJournal(journalPath);
        using var dataLease = AcquireDataLease(journal.DataDirectory);
        if (journal.Phase is not (UpdatePhase.RecoveryRequired or UpdatePhase.MigrationStarted or UpdatePhase.Installed)) throw new IOException("This update cannot be resumed.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(journal.InstallationDirectory, "release-info.json")));
        var version = manifest.RootElement.GetProperty("version").GetString();
        if (version is null || ReleaseVersion.Parse(version) is not { } current || ReleaseVersion.Parse(journal.Version) is not { } required || current.CompareTo(required) < 0) throw new IOException("Resume requires the same or a newer Winnow version.");
        if (manifest.RootElement.GetProperty("runtime").GetString() != journal.Runtime) throw new IOException("Resume runtime does not match.");
        journal.Phase = UpdatePhase.Installed; journal.Failure = null; Write(journalPath, journal);
    }
}
