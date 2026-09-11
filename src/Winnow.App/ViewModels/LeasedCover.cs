using Avalonia.Threading;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One surface's cover art, held under a lease for as long as the surface draws
/// it. The art is handed to the caller's own property through
/// <c>apply</c>, so a view model keeps the bitmap property its markup already
/// binds and gains only the lease.
///
/// <para>A lease is what stops the decoded-memory cache disposing pixels a
/// surface is still drawing (<see cref="ICoverLease"/>), so the rule is
/// symmetrical: whatever a view model shows, it shows through one of these, and
/// it disposes this when the surface goes away. Disposal clears the caller's
/// property first and releases the lease second, in that order, because the
/// pixels may be freed the moment the lease goes.</para>
///
/// <para><see cref="CoverPresenter"/> is the equivalent for the wall, the list
/// rows and the feed cards: it observes, cross-fades two layers and survives
/// container recycling. This is the plain version for a surface that shows one
/// game once — the detail modal, the screenshot strip and its lightbox, an IGDB
/// candidate row, a metadata preview.</para>
/// </summary>
internal sealed class LeasedCover : IDisposable
{
    private readonly ICoverLeases? _leases;
    private readonly CoverKey? _key;
    private readonly CoverLayers _layers;
    private readonly Action<CoverArt?> _apply;
    private readonly Action<Action> _post;

    /// <summary>The lease behind what the surface is drawing.</summary>
    private ICoverLease? _held;

    /// <summary>
    /// Leases whose load has not landed yet. Tracked, not just started: a modal
    /// closed while its cover is still decoding would otherwise leave a lease
    /// nobody releases, which is decoded art the cache may never free.
    /// </summary>
    private readonly List<ICoverLease> _loading = [];

    private int _requestedWidth;
    private int _generation;
    private bool _disposed;

    /// <param name="apply">
    /// Writes the art onto the surface, and null to clear it. Always called on
    /// the UI thread.
    /// </param>
    /// <param name="post">
    /// How a completed load reaches the UI thread. Defaults to the Avalonia
    /// dispatcher; tests pass an inline queue so no dispatcher has to exist.
    /// </param>
    public LeasedCover(
        ICoverLeases? leases,
        CoverKey? key,
        CoverLayers layers,
        Action<CoverArt?> apply,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(apply);

        _leases = leases;
        _key = key;
        _layers = layers;
        _apply = apply;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
    }

    /// <summary>False when there is no cache or no key, so this surface will never get art.</summary>
    public bool CanLoad => _leases is not null && _key is not null;

    /// <summary>
    /// Asks for the art at the width it will be drawn at. A memory hit applies
    /// synchronously; anything else arrives through <c>post</c>. A request at or
    /// below the width bucket already asked for is dropped, so being called
    /// again on attach or on a re-render costs nothing.
    /// </summary>
    public void Request(double displayWidthPixels)
    {
        if (_disposed || _leases is null || _key is not { } key || displayWidthPixels <= 0)
        {
            return;
        }

        var width = CoverImaging.SnapWidth(displayWidthPixels);
        if (width <= _requestedWidth)
        {
            return;
        }

        _requestedWidth = width;
        var generation = ++_generation;
        var lease = _leases.Acquire(key, width, _layers);

        if (lease.TryGetArt(out var hit))
        {
            Settle(lease, hit, generation);
            return;
        }

        _loading.Add(lease);
        _ = LoadAsync(lease, generation);
    }

    /// <summary>
    /// The surface is done with this art. Clears the caller's property and
    /// releases the lease, which is what allows the pixels to be freed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;

        // Property first, leases second: the pixels may be freed the moment the
        // last lease goes, so nothing may still be bound to them (CoverArt).
        _apply(null);

        foreach (var lease in _loading)
        {
            lease.Dispose();
        }

        _loading.Clear();
        _held?.Dispose();
        _held = null;
    }

    private async Task LoadAsync(ICoverLease lease, int generation)
    {
        CoverArt? art = null;
        try
        {
            art = await lease.GetAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The cache is contracted not to throw; a cover may not take a screen down.
        }

        _post(() => Settle(lease, art, generation));
    }

    private void Settle(ICoverLease lease, CoverArt? art, int generation)
    {
        _loading.Remove(lease);

        if (art is null || _disposed || generation != _generation)
        {
            if (art is null && !_disposed && generation == _generation)
                _requestedWidth = _held?.Width ?? 0;
            lease.Dispose();
            return;
        }

        var previous = _held;
        _held = lease;
        _apply(art);
        previous?.Dispose();
    }
}
