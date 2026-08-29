using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One surface's view-local cover state: the loaded pair, the widths in flight
/// and the lease behind what is on screen. Never shared between surfaces — the
/// tile model holds the identity (which work, which <see cref="CoverKey"/>, and
/// the lease source); this holds what is on screen for exactly one consumer.
/// </summary>
public sealed class CoverPresenter : ObservableObject, IDisposable
{
    private readonly Action<Action> _post;

    /// <summary>Widths with a load in flight for the current generation.</summary>
    private readonly HashSet<int> _pending = [];

    private ICoverLeases? _leases;
    private CoverKey? _key;

    /// <summary>The lease behind <see cref="Art"/>; kept so the pool entry outlives the load.</summary>
    private ICoverLease? _held;

    private CancellationTokenSource? _cancellation;
    private CoverArt? _art;
    private int _generation;
    private int _presentedWidth;
    private bool _disposed;

    /// <param name="post">
    /// How a completed load reaches the UI thread. Defaults to the Avalonia
    /// dispatcher; tests pass an inline queue so results land in the order the
    /// test states and no dispatcher has to exist.
    /// </param>
    public CoverPresenter(Action<Action>? post = null)
        => _post = post ?? (action => Dispatcher.UIThread.Post(action));

    /// <summary>The decoded pair, or null while this surface has no art for its game.</summary>
    public CoverArt? Art
    {
        get => _art;
        private set
        {
            // Reference identity, not the record's value equality: two decoded
            // pairs are two sets of pixels to hand the renderer even when their
            // fields compare equal.
            if (ReferenceEquals(_art, value))
            {
                return;
            }

            _art = value;
            OnPropertyChanged(nameof(Art));
            OnPropertyChanged(nameof(Vivid));
            OnPropertyChanged(nameof(Floor));
            OnPropertyChanged(nameof(HasCover));
            OnPropertyChanged(nameof(ShowPlaceholder));
        }
    }

    /// <summary>Vivid layer, decoded at display resolution. Null until art arrives.</summary>
    public Bitmap? Vivid => _art?.Vivid;

    /// <summary>Floor variant (sat 0.22 / bright 0.60), pre-computed by the cover cache.</summary>
    public Bitmap? Floor => _art?.Floor;

    public bool HasCover => _art is not null;

    /// <summary>Procedural art is the fallback: it paints whenever no cover is loaded (§7).</summary>
    public bool ShowPlaceholder => _art is null;

    /// <summary>The width bucket currently on screen; 0 when nothing is.</summary>
    public int PresentedWidth => _presentedWidth;

    /// <summary>
    /// Points this surface at a game. A different cover identity bumps the
    /// generation: everything in flight for the old one is abandoned and its
    /// results cannot land here afterwards.
    /// </summary>
    public void Target(CoverKey? key, ICoverLeases? leases)
    {
        if (_disposed)
        {
            return;
        }

        if (Nullable.Equals(_key, key) && ReferenceEquals(_leases, leases))
        {
            return;
        }

        Release();
        _key = key;
        _leases = leases;
    }

    /// <summary>
    /// Asks for art at a display width. A memory hit applies synchronously so
    /// scrolling back never flashes the placeholder; anything else arrives via
    /// <c>post</c> and is checked against the generation and the size bucket
    /// before it is allowed on screen (§5.1 — art never blocks the UI).
    /// Requests at or below the already-presented bucket are never made.
    /// </summary>
    public void Request(double displayWidthPixels)
    {
        if (_disposed || _leases is null || _key is not { } key)
        {
            return;
        }

        var width = CoverImaging.SnapWidth(displayWidthPixels);

        // Already showing this bucket or a larger one: a smaller cut of the same
        // art is never worth a re-decode, and never worth a downgrade.
        if (_art is not null && width <= _presentedWidth)
        {
            return;
        }

        if (!_pending.Add(width))
        {
            return;
        }

        var generation = _generation;
        var lease = _leases.Acquire(key, width);

        if (lease.TryGetArt(out var hit))
        {
            Settle(lease, hit, generation, width);
            return;
        }

        _cancellation ??= new CancellationTokenSource();
        _ = LoadAsync(lease, generation, width, _cancellation.Token);
    }

    /// <summary>
    /// This surface is done showing its game — recycled out of the wall, or
    /// navigated away from. Drops the art and the lease so the memory cache is
    /// the only owner of decoded pixels again, and retires the generation so
    /// nothing in flight can repaint a container that has moved on.
    /// </summary>
    public void Release()
    {
        _generation++;
        _pending.Clear();

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;

        _held?.Dispose();
        _held = null;

        _presentedWidth = 0;
        Art = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Release();
        _disposed = true;
    }

    private async Task LoadAsync(ICoverLease lease, int generation, int width, CancellationToken ct)
    {
        CoverArt? art = null;
        try
        {
            art = await lease.GetAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The cache is contracted not to throw; a cover may not take a screen down.
        }

        _post(() => Settle(lease, art, generation, width));
    }

    private void Settle(ICoverLease lease, CoverArt? art, int generation, int width)
    {
        if (generation == _generation)
        {
            _pending.Remove(width);
        }

        // Three ways a result loses: no art, a generation this surface has
        // retired, or a bucket smaller than what is already on screen.
        if (art is null || generation != _generation || (_art is not null && width < _presentedWidth))
        {
            lease.Dispose();
            return;
        }

        var previous = _held;
        _held = lease;
        _presentedWidth = width;
        Art = art;
        previous?.Dispose();
    }
}
