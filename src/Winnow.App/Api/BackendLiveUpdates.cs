using System.Threading.Channels;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Protocol;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Api;

/// <summary>Coalesces invalidations while retaining another refresh for changes arriving during a read.</summary>
public sealed class BackendLiveUpdates(WinnowApiClient api, IServiceProvider services,
    ILogger<BackendLiveUpdates> logger) : BackgroundService
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private int _pending;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(ReceiveAsync(stoppingToken), RefreshAsync(stoppingToken));

    private async Task ReceiveAsync(CancellationToken ct)
    {
        try
        {
            // The stream's initial resync arrives after subscription registration. Reads start afterwards.
            await foreach (var change in api.WatchEventsAsync(ct: ct))
            {
                var flags = Flags(change);
                if (flags == 0) continue;
                if ((flags & Preferences) != 0) services.GetService<ApiPresentationSettings>()?.Invalidate();
                Interlocked.Or(ref _pending, flags);
                _wake.Writer.TryWrite(true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _wake.Reader.ReadAllAsync(ct))
            {
                var flags = Interlocked.Exchange(ref _pending, 0);
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var window = services.GetRequiredService<MainWindowViewModel>();
                        if ((flags & Diagnostics) != 0
                            && services.GetService<ConnectionSessionWatcherHealth>() is { } health)
                            await health.RefreshAsync(ct);
                        if ((flags & Preferences) != 0)
                        {
                            // Preference controls save immediately. Wait for this client's current choice
                            // before applying another client's committed values; leave draft editors alone.
                            await window.LibrarySettings.PendingSave;
                            await window.LibrarySettings.RefreshAsync(ct);
                            await window.Display.PendingSave;
                            await window.Display.LoadAsync(ct);
                            if (services.GetService<ArtworkOrderViewModel>() is { } artwork)
                                await artwork.LoadAsync(ct);
                            if (services.GetService<ApplicationUpdater>() is { } updater)
                                await updater.RefreshPreferencesAsync(ct);
                            if (services.GetService<ThemeService>() is { } themes)
                            {
                                await themes.PendingSave;
                                await themes.LoadAsync(ct);
                            }
                            window.Library.ShowExplicitContent = window.LibrarySettings.ShowExplicitContent;
                        }
                        if ((flags & Library) != 0)
                        {
                            await window.Library.RefreshCommittedAsync(ct);
                            await window.MergeQueue.NoteQueueMayHaveMovedAsync(ct);
                            // TilesChanged schedules both the desktop feed and independent fullscreen
                            // library/feed. FullscreenContext preserves its own navigation and pending drafts.
                        }
                        else if ((flags & Feed) != 0)
                        {
                            await window.Feed.LoadCommand.ExecuteAsync(null);
                        }
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Refreshing frontend snapshots after a backend event failed.");
                    // Retain the invalidation even if the backend restarts between event delivery and read.
                    Interlocked.Or(ref _pending, flags);
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    _wake.Writer.TryWrite(true);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private const int Library = 1;
    private const int Preferences = 2;
    private const int Feed = 4;
    private const int Diagnostics = 8;
    internal static int Flags(BackendEvent change)
        => change.Kind switch
        {
            "resync-required" => Library | Preferences | Feed | Diagnostics,
            "diagnostics.changed" => Diagnostics,
            "preferences.changed" => Library | Preferences | Feed,
            "connections.changed" or "plugins.changed" => Library | Preferences | Feed,
            "feed.changed" => Library | Feed,
            "library.changed" when change.Resource?.StartsWith("preferences", StringComparison.Ordinal) == true
                => Library | Preferences | Feed,
            "library.changed" or "identity.changed" or "session.ended" => Library | Feed,
            _ => 0,
        };
}
