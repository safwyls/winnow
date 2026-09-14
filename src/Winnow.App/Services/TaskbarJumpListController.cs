using Winnow.App.ViewModels;

namespace Winnow.App.Services;

/// <summary>Publishes a snapshot of recent launchable games without blocking library refresh.</summary>
internal sealed class TaskbarJumpListController : IDisposable
{
    private readonly LibraryViewModel _library;
    private readonly string _directory;
    private readonly SemaphoreSlim _publishing = new(1);
    private int _revision;
    private bool _disposed;

    public TaskbarJumpListController(LibraryViewModel library, string directory)
    {
        _library = library;
        _directory = directory;
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
        var revision = Interlocked.Increment(ref _revision);
        _ = Task.Run(async () =>
        {
            await _publishing.WaitAsync();
            try
            {
                if (!_disposed && revision == Volatile.Read(ref _revision))
                    WindowsJumpList.Publish(_directory, games);
            }
            finally { _publishing.Release(); }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _revision);
        _library.TilesChanged -= OnTilesChanged;
    }
}
