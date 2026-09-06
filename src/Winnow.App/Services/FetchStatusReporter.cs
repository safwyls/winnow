using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Services;

/// <summary>
/// Marshals <see cref="EnrichmentProgress"/> from the background enrichment
/// thread onto the UI thread via <see cref="Dispatcher.UIThread"/>.
///
/// <para><see cref="FetchStatusViewModel"/> is deliberately dispatcher-free
/// so it can be tested as a plain object. This class is the bridge: it
/// receives <see cref="IProgress{T}.Report"/> on whichever thread the
/// enrichment pass happens to be running on and posts the update to the
/// dispatcher, keeping the two concerns — what the field says, and how the
/// report reaches it — in separate types.</para>
/// </summary>
public sealed class FetchStatusReporter : IProgress<EnrichmentProgress>
{
    private readonly FetchStatusViewModel _status;

    public FetchStatusReporter(FetchStatusViewModel status) => _status = status;

    public void Report(EnrichmentProgress value)
    {
        var remaining = value.Remaining;
        var total = value.Total;

        Dispatcher.UIThread.Post(() =>
        {
            if (remaining <= 0)
            {
                _status.Clear();
            }
            else if (!_status.IsActive)
            {
                _status.Begin(total);
                _status.Report(remaining);
            }
            else
            {
                _status.Report(remaining);
            }
        });
    }
}
