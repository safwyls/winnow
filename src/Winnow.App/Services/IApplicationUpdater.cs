namespace Winnow.App.Services;

public sealed record UpdateSnapshot(
    bool Automatic = true, bool IncludeBeta = false, bool Busy = false,
    bool CanDownload = false, bool CanRestart = false, double Progress = 0,
    string Status = "Updates have not been checked yet.", string? AvailableVersion = null,
    string? ReleaseUrl = null, string? DownloadUrl = null, bool CanCancel = false);

public interface IApplicationUpdater
{
    event EventHandler? Changed;
    UpdateSnapshot Snapshot { get; }
    Task CheckAsync(CancellationToken ct = default);
    Task DownloadAsync(CancellationToken ct = default);
    void CancelDownload();
    Task RestartAsync(CancellationToken ct = default);
    Task SetAutomaticAsync(bool value, CancellationToken ct = default);
    Task SetIncludeBetaAsync(bool value, CancellationToken ct = default);
}
