using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>One update state shared by desktop, fullscreen, and the background scheduler.</summary>
internal sealed class ApplicationUpdater(
    GitHubReleaseClient releases, ISettingsRepository settings, IUpdateInstaller installer,
    string cacheDirectory, string currentVersion, string assetSuffix, Action requestShutdown,
    ILogger<ApplicationUpdater> logger) : BackgroundService, IApplicationUpdater
{
    internal const string AutomaticKey = "application.updates.automatic";
    internal const string BetaKey = "application.updates.include_beta";
    private readonly SemaphoreSlim _operation = new(1, 1);
    private CancellationTokenSource? _downloadCancellation;
    private CancellationTokenSource? _checkCancellation;
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateSnapshot _snapshot = new();
    private bool _loaded;
    private ApplicationRelease? _release;
    private string? _staged;

    public event EventHandler? Changed;
    public UpdateSnapshot Snapshot => Volatile.Read(ref _snapshot);

    private void Publish(UpdateSnapshot value)
    {
        Volatile.Write(ref _snapshot, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Load preferences promptly; network work waits until startup has settled.
            await _operation.WaitAsync(stoppingToken);
            try { await LoadAsync(stoppingToken); }
            finally { _operation.Release(); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            do
            {
                if (Snapshot.Automatic) await CheckAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background application updates could not start.");
            Publish(Snapshot with { Status = "Automatic updates could not start. Try checking manually." });
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (_loaded) return;
        var automatic = await settings.GetAsync(AutomaticKey, ct);
        var beta = await settings.GetAsync(BetaKey, ct);
        CleanAbandonedDownloads();
        Publish(Snapshot with { Automatic = automatic != "false", IncludeBeta = beta == "true" });
        _loaded = true;
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct)) return;
        using var checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        Volatile.Write(ref _checkCancellation, checkCancellation);
        try
        {
            await LoadAsync(ct);
            var current = ReleaseVersion.Parse(currentVersion);
            if (current is null || current.IsDevelopment)
            {
                Publish(Snapshot with { Status = "Development and CI builds do not receive release updates." });
                return;
            }
            if (string.IsNullOrEmpty(assetSuffix))
            {
                Publish(Snapshot with { Status = "Automatic updates are not available for this platform." });
                return;
            }
            Publish(Snapshot with { Busy = true, Status = "Checking for updates…" });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(checkCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var found = await releases.FindAsync(current, Snapshot.IncludeBeta, assetSuffix, timeout.Token);
            if (_release?.Version.Text != found?.Version.Text || _release?.Sha256 != found?.Sha256)
            {
                DeleteStaged();
                _release = found;
            }
            if (found is null)
            {
                Publish(Snapshot with { Busy = false, CanDownload = false, CanRestart = false,
                    AvailableVersion = null, ReleaseUrl = null, DownloadUrl = null,
                    Status = "Winnow is up to date for this channel." });
                return;
            }
            Publish(Snapshot with { AvailableVersion = found.Version.Text, ReleaseUrl = found.ReleaseUrl,
                DownloadUrl = found.DownloadUrl, CanDownload = installer.IsSupported && found.Sha256 is not null && _staged is null,
                CanRestart = _staged is not null, Status = _staged is not null ? "Update ready. Restart when you are ready."
                    : !installer.IsSupported ? "A new version is available. Download it to update this installation."
                    : found.Size == 0 ? "This release has no usable package for this installation. Open the release page."
                    : found.Sha256 is null ? "This release has no verification digest. Use the release page to update manually."
                    : "A new version is available." });
            checkCancellation.Token.ThrowIfCancellationRequested();
            if (Snapshot.Automatic && Snapshot.CanDownload) await DownloadCoreAsync(checkCancellation.Token);
        }
        catch (Exception ex) { ReportFailure(ex, ct); }
        finally { Volatile.Write(ref _checkCancellation, null); Publish(Snapshot with { Busy = false }); _operation.Release(); }
    }

    public async Task DownloadAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct)) return;
        try { await DownloadCoreAsync(ct); }
        catch (Exception ex) { ReportFailure(ex, ct); }
        finally { Publish(Snapshot with { Busy = false }); _operation.Release(); }
    }

    private async Task DownloadCoreAsync(CancellationToken ct)
    {
        if (_release is not { Sha256: not null } release || !installer.IsSupported || _staged is not null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(30));
        Volatile.Write(ref _downloadCancellation, cancellation);
        string? partial = null;
        try
        {
            Publish(Snapshot with { Busy = true, CanCancel = true, CanRestart = false, Progress = 0, Status = "Downloading update…" });
            Directory.CreateDirectory(cacheDirectory);
            partial = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}.partial");
            var lastProgress = -1;
            await releases.DownloadAsync(release, partial, progress =>
            {
                var percent = (int)progress;
                if (percent == lastProgress) return;
                lastProgress = percent;
                Publish(Snapshot with { Progress = progress });
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var staged = Path.ChangeExtension(partial, ".exe");
            File.Move(partial, staged);
            _staged = staged;
            Publish(Snapshot with { CanDownload = false, CanRestart = true, Progress = 100,
                Status = "Update ready. Restart when you are ready." });
        }
        finally
        {
            Volatile.Write(ref _downloadCancellation, null);
            Publish(Snapshot with { CanCancel = false });
            if (partial is not null) TryDelete(partial);
        }
    }

    public void CancelDownload()
    {
        try { Volatile.Read(ref _downloadCancellation)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        if (!await _operation.WaitAsync(0, ct)) return;
        try
        {
            if (_staged is null || _release?.Sha256 is not { } hash || !installer.IsSupported) return;
            Publish(Snapshot with { Busy = true, Status = "Preparing to restart…" });
            await installer.PrepareAsync(_staged, hash, ct);
            requestShutdown();
        }
        catch (Exception ex)
        {
            DeleteStaged();
            Publish(Snapshot with { CanRestart = false, CanDownload = _release?.Sha256 is not null && installer.IsSupported });
            ReportFailure(ex, ct);
        }
        finally { Publish(Snapshot with { Busy = false }); _operation.Release(); }
    }

    public Task SetAutomaticAsync(bool value, CancellationToken ct = default) => SetPreferenceAsync(false, value, ct);
    public Task SetIncludeBetaAsync(bool value, CancellationToken ct = default) => SetPreferenceAsync(true, value, ct);

    private async Task SetPreferenceAsync(bool beta, bool value, CancellationToken ct)
    {
        if (_loaded && (beta ? Snapshot.IncludeBeta : Snapshot.Automatic) == value) return;
        try { Volatile.Read(ref _checkCancellation)?.Cancel(); }
        catch (ObjectDisposedException) { }
        CancelDownload();
        await _operation.WaitAsync(ct);
        var recheck = false;
        try
        {
            await LoadAsync(ct);
            await settings.SetAsync(beta ? BetaKey : AutomaticKey, value ? "true" : "false", ct);
            if (beta)
            {
                DeleteStaged();
                _release = null;
                Publish(Snapshot with { IncludeBeta = value, CanDownload = false, CanRestart = false, Progress = 0,
                    AvailableVersion = null, ReleaseUrl = null, DownloadUrl = null, Status = "Update channel changed." });
            }
            else Publish(Snapshot with { Automatic = value });
            recheck = Snapshot.Automatic;
        }
        catch (Exception ex) { ReportFailure(ex, ct); }
        finally { _operation.Release(); }
        if (recheck) await CheckAsync(ct);
    }

    private void ReportFailure(Exception ex, CancellationToken ct)
    {
        logger.LogWarning(ex, "Application update operation did not complete.");
        Publish(Snapshot with { Status = ex is OperationCanceledException
                ? ct.IsCancellationRequested || _lifetime.IsCancellationRequested ? "Update cancelled." : "Update stopped or timed out. Try again when ready."
                : ex is InvalidDataException ? "The update could not be verified. Try again or open the release page."
                : "Couldn't complete the update. Check your connection and try again, or open the release page." });
    }

    private void DeleteStaged()
    {
        if (_staged is not null) TryDelete(_staged);
        _staged = null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void CleanAbandonedDownloads()
    {
        if (!Directory.Exists(cacheDirectory)) return;
        try
        {
            if ((File.GetAttributes(cacheDirectory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                if (Path.GetExtension(file) is not (".partial" or ".exe")
                    || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)
                    || File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-2)) continue;
                TryDelete(file);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifetime.CancelAsync();
        CancelDownload();
        await base.StopAsync(cancellationToken);
        await _operation.WaitAsync(cancellationToken);
        _operation.Release();
    }
}
