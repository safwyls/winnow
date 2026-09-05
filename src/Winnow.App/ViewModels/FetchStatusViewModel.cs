using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>
/// The rail's fetch status field (§8): words naming what the enrichment pass
/// has left to do, shown as a real count that falls a slice at a time.
///
/// <para>Deliberately free of any <c>Dispatcher</c> call, so a test can drive
/// it on its own thread. <see cref="Services.FetchStatusReporter"/> is the
/// piece that marshals — it takes the enrichment pass's report off the
/// background thread and onto the UI thread.</para>
///
/// <para>The field never appears when there is nothing to do:
/// <see cref="Begin"/> returns early on a zero total, so a warm library shows
/// nothing at all. It is cleared in a <c>finally</c> inside the pass, so a
/// run cut short by shutdown takes the field away rather than leaving a stale
/// count on screen.</para>
/// </summary>
public partial class FetchStatusViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingText), nameof(RemainingNote), nameof(AutomationName))]
    public partial int Remaining { get; set; }

    public string Label => FetchStatusCopy.Label;

    public string RemainingText => Remaining.ToString("N0");

    public string RemainingNote => FetchStatusCopy.Remaining(Remaining);

    public string AutomationName => FetchStatusCopy.AutomationName(Remaining);

    /// <summary>
    /// Opens the field with the total backlog. A zero or negative total is
    /// treated as "nothing to do" and the field stays hidden, so a warm
    /// library — every launch after the first — never raises the indicator.
    /// </summary>
    public void Begin(int total)
    {
        if (total <= 0)
        {
            Clear();
            return;
        }

        Remaining = total;
        IsActive = true;
    }

    /// <summary>
    /// Updates the count. Ignored after <see cref="Clear"/> so a late
    /// report from a cancelled slice cannot resurrect the field.
    /// Reaching zero is what "done" means on this channel; there is no
    /// separate completion signal.
    /// </summary>
    public void Report(int remaining)
    {
        if (!IsActive)
        {
            return;
        }

        if (remaining <= 0)
        {
            Clear();
            return;
        }

        Remaining = remaining;
    }

    /// <summary>Hides the field and resets the count.</summary>
    public void Clear()
    {
        IsActive = false;
        Remaining = 0;
    }
}
