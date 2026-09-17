using Winnow.App.ViewModels;
using Winnow.Covers;

namespace Winnow.App.Services;

/// <summary>Publishes a snapshot of recent launchable games without blocking library refresh.</summary>
internal sealed class TaskbarJumpListController : IDisposable
{
    private readonly LibraryViewModel _library;
    private readonly string _directory;
    private readonly SemaphoreSlim _publishing = new(1);
    private int _revision;
    private bool _disposed;
    private readonly JumpListIcons? _icons;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<CoverKey, string> _knownIcons = [];

    public TaskbarJumpListController(LibraryViewModel library, string directory, CoverPipeline? covers = null)
    {
        _library = library;
        _directory = directory;
        _icons = covers is null ? null : new JumpListIcons(directory, covers);
        library.TilesChanged += OnTilesChanged;
        Publish();
    }

    internal static IReadOnlyList<JumpListGame> RecentGames(IEnumerable<GameTileViewModel> tiles) => tiles
        .Where(tile => tile.LastPlayedUtc is not null && tile.IsPlayAction)
        .OrderByDescending(tile => tile.LastPlayedUtc)
        .ThenBy(tile => tile.Title, StringComparer.OrdinalIgnoreCase)
        .Take(10)
        .Select(tile => new JumpListGame(tile.PlayableEntry.OwnershipId, tile.Title))
        .ToArray();

    private void OnTilesChanged(object? sender, EventArgs e) => Publish();

    private void Publish()
    {
        if (_disposed || !OperatingSystem.IsWindows()) return;
        var games = RecentGames(_library.AllTiles);
        var covers = games.ToDictionary(game => game.OwnershipId,
            game => _library.TileForOwnership(game.OwnershipId) is { } tile ? tile.IconKey ?? tile.CoverKey : null);
        var revision = Interlocked.Increment(ref _revision);
        _ = Task.Run(async () =>
        {
            await _publishing.WaitAsync();
            try
            {
                if (_disposed || revision != Volatile.Read(ref _revision)) return;
                // Publish tasks immediately; slow or missing artwork must not block launching.
                var initial = games.Select(game => covers.GetValueOrDefault(game.OwnershipId) is { } key
                    && _knownIcons.TryGetValue(key, out var cached) && File.Exists(cached)
                        ? game with { IconPath = cached } : game).ToArray();
                WindowsJumpList.Publish(_directory, initial);
                if (_icons is null || games.Count == 0) return;
                var decorated = new List<JumpListGame>();
                foreach (var game in games)
                {
                    if (_disposed || revision != Volatile.Read(ref _revision)) return;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    string? icon = null;
                    try { icon = await _icons.GetAsync(covers.GetValueOrDefault(game.OwnershipId), timeout.Token); }
                    catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested) { }
                    if (icon is not null && covers.GetValueOrDefault(game.OwnershipId) is { } key)
                        _knownIcons[key] = icon;
                    decorated.Add(game with { IconPath = icon });
                }
                if (!_disposed && revision == Volatile.Read(ref _revision))
                    WindowsJumpList.Publish(_directory, decorated);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            finally { _publishing.Release(); }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _shutdown.Cancel();
        Interlocked.Increment(ref _revision);
        _library.TilesChanged -= OnTilesChanged;
    }
}
