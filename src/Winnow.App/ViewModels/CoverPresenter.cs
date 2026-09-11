using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One surface's view-local cover state: the loaded pair, the widths in flight
/// and the lease behind what is on screen. Never shared between surfaces — the
/// tile model holds the identity (which work, which <see cref="CoverKey"/>, and
/// the lease source); this holds what is on screen for exactly one consumer.
///
/// <para>This is the two-layer surface: the floor variant under the vivid art,
/// the vivid layer's opacity carrying the §5.1 ramp. It asks the cache for both
/// layers only while the ramp is dimming anything — with dimming off the vivid
/// layer is drawn at full opacity and the floor is a bitmap nobody can see, so
/// the request drops to <see cref="CoverLayers.Vivid"/> and the wall holds one
/// decode per cover instead of two. Turning dimming back on re-requests the
/// pair, which is why the ramp is watched here rather than read once.</para>
/// </summary>
public sealed class CoverPresenter : ObservableObject, IDisposable
{
    private readonly Action<Action> _post;

    /// <summary>Widths with a load in flight for the current generation.</summary>
    private readonly HashSet<int> _pending = [];
    private readonly HashSet<ICoverLease> _loading = [];

    private ICoverLeases? _leases;
    private CoverKey? _key;
    private DormancyRamp? _ramp;

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

    /// <summary>
    /// Floor variant (§5.1's 0.22 / 0.68 endpoint), pre-computed by the cover
    /// cache. Null while the ramp is off and this surface asked for the vivid
    /// layer alone; an <c>Image</c> bound to null draws nothing, which is the
    /// same thing a vivid layer at full opacity leaves visible.
    /// </summary>
    public Bitmap? Floor => _art?.Floor;

    public bool HasCover => _art is not null;

    /// <summary>Procedural art is the fallback: it paints whenever no cover is loaded (§7).</summary>
    public bool ShowPlaceholder => _art is null;

    /// <summary>The width bucket currently on screen; 0 when nothing is.</summary>
    public int PresentedWidth => _presentedWidth;

    /// <summary>
    /// The layers this surface needs decoded: both while the ramp dims dormant
    /// covers, the vivid layer alone when the user has turned dimming off,
    /// because then the floor variant is never visible under it.
    /// </summary>
    public CoverLayers Layers => _ramp is null || _ramp.DimsDormantCovers
        ? CoverLayers.VividAndFloor
        : CoverLayers.Vivid;

    /// <summary>
    /// Points this surface at a game. A different cover identity bumps the
    /// generation: everything in flight for the old one is abandoned and its
    /// results cannot land here afterwards.
    /// </summary>
    /// <param name="ramp">
    /// The ramp this surface resolves dormancy through, so the presenter knows
    /// whether the floor layer is visible at all. Null asks for both layers,
    /// which is the safe answer for a surface with no ramp of its own.
    /// </param>
    public void Target(CoverKey? key, ICoverLeases? leases, DormancyRamp? ramp = null)
    {
        if (_disposed)
        {
            return;
        }

        if (Nullable.Equals(_key, key)
            && ReferenceEquals(_leases, leases)
            && ReferenceEquals(_ramp, ramp))
        {
            return;
        }

        Release();
        Watch(ramp);
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
        var layers = Layers;

        // Already showing this bucket or a larger one, with the layers this
        // surface draws: a smaller cut of the same art is never worth a
        // re-decode, and never worth a downgrade.
        if (_art is not null && width <= _presentedWidth && _art.Satisfies(layers))
        {
            return;
        }

        if (!_pending.Add(width))
        {
            return;
        }

        var generation = _generation;
        var lease = _leases.Acquire(key, width, layers);

        if (lease.TryGetArt(out var hit))
        {
            Settle(lease, hit, generation, width);
            return;
        }

        _cancellation ??= new CancellationTokenSource();
        _loading.Add(lease);
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

        // Art first, lease second: releasing the last lease on an evicted cover
        // frees its pixels, so the binding has to have let go of them before
        // that can happen (CoverArt states the invariant).
        _presentedWidth = 0;
        Art = null;

        _held?.Dispose();
        _held = null;
        foreach (var lease in _loading) lease.Dispose();
        _loading.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Release();
        Watch(null);
        _disposed = true;
    }

    /// <summary>
    /// One subscription per realized surface, not per tile: the wall has a few
    /// dozen containers alive at once and the ramp is a single shared object.
    /// </summary>
    private void Watch(DormancyRamp? ramp)
    {
        if (ReferenceEquals(_ramp, ramp))
        {
            return;
        }

        if (_ramp is not null)
        {
            _ramp.PropertyChanged -= OnRampChanged;
        }

        _ramp = ramp;

        if (_ramp is not null)
        {
            _ramp.PropertyChanged += OnRampChanged;
        }
    }

    /// <summary>
    /// The user turned dimming on while this surface was showing a vivid-only
    /// decode. The floor layer is visible again, so ask for the pair at the
    /// width already on screen; the vivid art stays up until it arrives.
    /// </summary>
    private void OnRampChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(DormancyRamp.DimsDormantCovers) or null))
        {
            return;
        }

        if (_disposed || (_art is not null && _art.Satisfies(Layers)))
        {
            return;
        }

        var width = Math.Max(_presentedWidth, _pending.DefaultIfEmpty(0).Max());
        if (width == 0) return;
        _pending.Remove(width);
        Request(width);
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
        _loading.Remove(lease);
        if (generation == _generation)
        {
            _pending.Remove(width);
        }

        // Three ways a result loses: no art, a generation this surface has
        // retired, or a bucket smaller than what is already on screen. A pair
        // arriving at the presented width is not a loser — that is the ramp
        // being turned back on under a vivid-only decode.
        if (art is null || generation != _generation || (_art is not null && width < _presentedWidth)
            || (_art is not null && _art.Satisfies(Layers) && !art.Satisfies(Layers)))
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
