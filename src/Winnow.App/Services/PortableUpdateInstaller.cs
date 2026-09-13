using System.Diagnostics;
using Winnow.Update;

namespace Winnow.App.Services;

internal sealed class PortableUpdateInstaller(string installationDirectory, string dataDirectory, bool leaseAvailable = true) : IUpdateInstaller
{
    private string? _transactionId;
    public bool IsSupported => leaseAvailable && UpdateInstallation.IsPortable(installationDirectory, Environment.ProcessPath,
        UpdateInstallation.Runtime, UpdateInstallation.OsRelease);

    public string? RecoveryStatus
    {
        get
        {
            var path = PortableUpdateEngine.GetJournalPath(installationDirectory);
            if (!File.Exists(path)) return null;
            try
            {
                var journal = PortableUpdateEngine.ReadJournal(path);
                return journal.Phase == UpdatePhase.Restored
                    ? "The previous version and its paired library backup were restored."
                    : journal.Phase == UpdatePhase.RecoveryRequired
                    ? "Update recovery needs attention. Keep your library and reinstall the same or a newer release."
                    : journal.Failure is not null
                    ? "The previous update did not finish. Download it again, or follow the portable recovery instructions in the release documentation."
                    : null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            { return "The update recovery record could not be read. Keep your library and reinstall the same or a newer release."; }
        }
    }

    public Task<string> StageAsync(string path, string sha256, string version, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!IsSupported) throw new InvalidOperationException("This installation requires a manual update.");
            _transactionId = Guid.NewGuid().ToString("N");
            var journal = PortableUpdateEngine.Stage(path, sha256, version, UpdateInstallation.Runtime,
                installationDirectory, dataDirectory, OperatingSystem.IsWindows() ? "Winnow.exe" : "Winnow",
                Environment.GetCommandLineArgs().Contains("--no-sync", StringComparer.Ordinal) ? ["--no-sync"] : [],
                transactionId: _transactionId);
            if (ct.IsCancellationRequested)
            {
                PortableUpdateEngine.DiscardStaged(journal, _transactionId);
                ct.ThrowIfCancellationRequested();
            }
            return journal;
        }, ct);

    public void Discard(string staged) => PortableUpdateEngine.DiscardStaged(staged, _transactionId);

    public async Task PrepareAsync(string installerPath, string sha256, CancellationToken ct = default)
    {
        if (!IsSupported) throw new InvalidOperationException("This installation requires a manual update.");
        var transaction = _transactionId ?? throw new IOException("The staged update no longer belongs to this run. Download it again.");
        var helperPath = PortableUpdateEngine.PrepareHelper(installerPath, transaction);
        var directory = Path.GetDirectoryName(installerPath)!;
        using var current = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(helperPath)
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = directory };
        foreach (var argument in new[] { "apply", "--journal", installerPath, "--pid", current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--start-ticks", current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--transaction", transaction })
            start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new IOException("The update helper could not start.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(directory, "helper-ready")) ||
                await File.ReadAllTextAsync(Path.Combine(directory, "helper-ready"), timeout.Token).ConfigureAwait(false) != transaction)
            {
                if (helper.HasExited) throw new IOException("The portable update helper could not prepare the restart.");
                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(directory, "proceed"), transaction, ct).ConfigureAwait(false);
        }
        catch
        {
            File.WriteAllText(Path.Combine(directory, "cancel"), transaction);
            throw;
        }
    }
}
