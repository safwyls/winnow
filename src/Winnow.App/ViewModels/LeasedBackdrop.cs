using Avalonia.Threading;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>Retains displayed pixels while trying ranked replacements and resolution upgrades.</summary>
internal sealed class LeasedBackdrop(ICoverLeases? leases, Action<CoverArt?> apply) : IDisposable
{
    private ICoverLease? _held;
    private ICoverLease? _pending;
    private IReadOnlyList<CoverKey> _keys = [];
    private IReadOnlyList<int> _widths = [];
    private int _index;
    private bool _disposed;

    public void Request(IReadOnlyList<CoverKey> keys, Func<CoverKey, double> displayWidthPixels)
    {
        if (_disposed || leases is null) return;
        var widths = keys.Select(key => CoverImaging.SnapWidth(displayWidthPixels(key))).ToArray();
        if (_keys.SequenceEqual(keys) && widths.Zip(_widths).All(pair => pair.First <= pair.Second)) return;
        _keys = keys;
        _widths = widths;
        _index = 0;
        _pending?.Dispose();
        _pending = null;
        Next();
    }

    private void Next()
    {
        if (_index >= _keys.Count)
        {
            apply(null);
            _held?.Dispose(); _held = null;
            return;
        }
        var lease = leases!.Acquire(_keys[_index], _widths[_index], CoverLayers.Vivid);
        _index++;
        _pending = lease;
        if (lease.TryGetArt(out var hit)) Settle(lease, hit);
        else _ = LoadAsync(lease);
    }

    private async Task LoadAsync(ICoverLease lease)
    {
        CoverArt? art = null;
        try { art = await lease.GetAsync().ConfigureAwait(false); }
        catch (Exception) { /* A missing asset advances to the next candidate. */ }
        Dispatcher.UIThread.Post(() => Settle(lease, art));
    }

    private void Settle(ICoverLease lease, CoverArt? art)
    {
        if (_disposed || !ReferenceEquals(_pending, lease)) return;
        _pending = null;
        if (art?.Vivid is null)
        {
            lease.Dispose();
            if (_held?.Key != lease.Key) Next();
            return;
        }
        var previous = _held;
        _held = lease;
        apply(art);
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        apply(null);
        _pending?.Dispose(); _pending = null;
        _held?.Dispose(); _held = null;
    }
}
