using Winnow.Enrich.Igdb;

namespace Winnow.App.Services;

public enum MetadataSyncResult { Completed, MissingCredentials, PartialFailure, RefreshFailed }

public interface IManualMetadataSyncService
{
    Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>Refreshes the existing library without acquiring ownership or changing automatic sync.</summary>
public sealed class ManualMetadataSyncService(IIgdbClient igdb, LibraryRefreshPipeline pipeline)
    : IManualMetadataSyncService
{
    public Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(async () =>
        {
            if (!await igdb.IsConfiguredAsync(ct).ConfigureAwait(false))
                return MetadataSyncResult.MissingCredentials;

            progress?.Report("Waiting for metadata sync…");
            var result = await pipeline.RunWithResultAsync(ct, igdbOnly: true, progress).ConfigureAwait(false);
            if (result.PublicationFailed) return MetadataSyncResult.RefreshFailed;
            return result.FailedSteps > 0 ? MetadataSyncResult.PartialFailure : MetadataSyncResult.Completed;
        }, ct);
}
