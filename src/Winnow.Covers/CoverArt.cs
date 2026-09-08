using Avalonia.Media.Imaging;

namespace Winnow.Covers;

/// <summary>
/// One cover's decoded layers: vivid art, and the desaturated floor variant
/// under it when the caller asked for both (<see cref="CoverLayers"/>).
///
/// <para><b>Who owns the pixels.</b> A decoded layer is native memory that the
/// garbage collector only returns when a finalizer runs, so the pixels are
/// released explicitly instead. Ownership is a hold count: the decoded-memory
/// LRU behind <see cref="CoverCache"/> takes the first hold when the art is
/// admitted, and <see cref="CoverLeasePool"/> takes one more for as long as any
/// surface holds a lease on the slot. The bitmaps are disposed when the last
/// hold goes, which means eviction frees them at once for art nobody is drawing
/// and defers to the last lease for art that is on screen.</para>
///
/// <para><b>Why disposal is posted.</b> The render thread composites the frame
/// the UI thread last committed, so a bitmap must not be disposed while that
/// frame can still reference it. The dispose is therefore posted back to the UI
/// thread at background priority: by the time it runs, the consumer that
/// dropped its lease has already cleared the binding and the compositor has the
/// frame that no longer draws it. Never dispose a layer directly.</para>
/// </summary>
public sealed class CoverArt
{
    private readonly Lock _gate = new();
    private readonly Action<Action>? _post;

    /// <summary>Starts at the hold taken by whoever decoded and cached the art.</summary>
    private int _holds = 1;

    /// <param name="vivid">Vivid layer, decoded at display resolution.</param>
    /// <param name="floor">
    /// The floor variant, or <see langword="null"/> for a vivid-only request.
    /// </param>
    public CoverArt(Bitmap vivid, Bitmap? floor)
        : this(vivid, floor, null)
    {
    }

    /// <param name="post">
    /// How disposal reaches the UI thread. <see langword="null"/> disposes in
    /// place, which is right for a caller with no dispatcher — a test, or a
    /// pair of bitmaps that were never handed to a control.
    /// </param>
    internal CoverArt(Bitmap vivid, Bitmap? floor, Action<Action>? post)
    {
        Vivid = vivid;
        Floor = floor;
        _post = post;
    }

    /// <summary>Vivid layer. Always present.</summary>
    public Bitmap Vivid { get; }

    /// <summary>
    /// Floor variant (§5.1's 0.22 / 0.68 endpoint with the −6° rotation baked
    /// in), or null when the request was <see cref="CoverLayers.Vivid"/>. A
    /// surface that stacks two layers binds this and draws nothing for null.
    /// </summary>
    public Bitmap? Floor { get; }

    /// <summary>What this art actually carries, which can be more than was asked for.</summary>
    public CoverLayers Layers => Floor is null ? CoverLayers.Vivid : CoverLayers.VividAndFloor;

    /// <summary>Whether this art can answer a request for <paramref name="layers"/>.</summary>
    public bool Satisfies(CoverLayers layers)
        => layers != CoverLayers.VividAndFloor || Floor is not null;

    /// <summary>
    /// Takes a hold, or answers false when the last hold has already gone and
    /// the pixels are on their way out. A caller that gets false must treat the
    /// art as a cache miss and ask again rather than draw it.
    /// </summary>
    internal bool TryHold()
    {
        lock (_gate)
        {
            if (_holds == 0)
            {
                return false;
            }

            _holds++;
            return true;
        }
    }

    /// <summary>Drops one hold, disposing both layers when it was the last.</summary>
    internal void ReleaseHold()
    {
        lock (_gate)
        {
            if (_holds == 0 || --_holds > 0)
            {
                return;
            }
        }

        if (_post is null)
        {
            Free();
            return;
        }

        _post(Free);
    }

    /// <summary>Holds currently taken. Diagnostics and tests only.</summary>
    internal int Holds
    {
        get
        {
            lock (_gate)
            {
                return _holds;
            }
        }
    }

    private void Free()
    {
        Vivid?.Dispose();
        Floor?.Dispose();
    }
}
