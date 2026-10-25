namespace Winnow.App.Services;

public enum MetadataSyncResult { Completed, MissingCredentials, PartialFailure, RefreshFailed }

public interface IManualMetadataSyncService
{
    Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
