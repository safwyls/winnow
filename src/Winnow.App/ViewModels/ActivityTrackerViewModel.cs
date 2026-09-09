using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>Selection and copy for the two views of one ownership's recorded play.</summary>
public sealed partial class ActivityTrackerViewModel : ObservableObject
{
    private readonly IReadOnlyList<PlaytimeSnapshot> _snapshots;
    private readonly IReadOnlyList<Session> _sessions;
    private readonly DateTime? _acquiredUtc;
    private readonly DateTime _nowUtc;
    private readonly string _scope;

    public ActivityTrackerViewModel(
        IReadOnlyList<PlaytimeSnapshot> snapshots,
        IReadOnlyList<Session> sessions,
        DateTime? acquiredUtc,
        DateTime? lastPlayedUtc,
        long totalMinutes,
        DateTime nowUtc,
        string scope = "")
    {
        _snapshots = snapshots;
        _sessions = sessions;
        _acquiredUtc = acquiredUtc;
        _nowUtc = nowUtc;
        _scope = scope;
        HasTrackedSessions = ActivityTimelineSeries.Build(snapshots, sessions, acquiredUtc,
            lastPlayedUtc, nowUtc, trackedSessions: true).HasHistory;
        LastPlayedUtc = lastPlayedUtc is { } at ? UpdateEventViewModel.AsUtc(at) : null;
        TotalText = GameDetailsViewModel.SpanText(totalMinutes);
        LastPlayedText = LastPlayedUtc is { } played
            ? $"Last played {UpdateEventViewModel.LocalDateText(played)}"
            : totalMinutes > 0 ? "Last-played date unavailable" : "No playtime recorded";
        Rebuild();
    }

    public string TotalText { get; }
    public string TotalLabel => _scope.Length > 0 ? $"played · {_scope}" : "total played";
    public DateTime? LastPlayedUtc { get; }
    public string LastPlayedText { get; }
    public bool HasTrackedSessions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLifetime))]
    [NotifyPropertyChangedFor(nameof(TrackedLegend))]
    public partial bool IsTrackedSessions { get; set; }

    public bool IsLifetime => !IsTrackedSessions;
    public string TrackedLegend => IsTrackedSessions ? "Winnow · sessions" : "Winnow · monthly totals";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlot))]
    [NotifyPropertyChangedFor(nameof(ShowHistoricalLegend))]
    [NotifyPropertyChangedFor(nameof(ScaleText))]
    [NotifyPropertyChangedFor(nameof(HasRecords))]
    public partial ActivityTimelineSeries Series { get; private set; }
        = ActivityTimelineSeries.Build([], [], null, null, DateTime.UnixEpoch);

    public bool ShowPlot => Series.EndUtc > Series.StartUtc;
    public bool ShowHistoricalLegend => !IsTrackedSessions && Series.Bars.Any(bar => !bar.IsTracked);
    public bool HasRecords => Series.Bars.Count > 0;
    public string ScaleText => Series.MaxHours > 0
        ? $"{Series.MaxHours:0.##}h · tallest bar"
        : "No recorded hours in this view";

    [ObservableProperty]
    public partial string DetailText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<UpdateEventViewModel> Updates { get; private set; } = [];

    [ObservableProperty]
    public partial string UpdateSummary { get; private set; } = string.Empty;

    public void RefreshUpdates(IReadOnlyList<UpdateEventViewModel> updates)
    {
        // A new collection also invalidates mark groups after acknowledgement.
        Updates = updates.ToArray();
        var unread = updates.Count(update => update.IsUnread);
        UpdateSummary = unread > 0
            ? $"{unread} unread update{(unread == 1 ? "" : "s")}"
            : updates.Count > 0 ? "No unread updates" : "No updates recorded";
    }

    public void SelectDetail(string text) => DetailText = text;

    [RelayCommand]
    private void ShowLifetime()
    {
        IsTrackedSessions = false;
        OnPropertyChanged(nameof(IsLifetime));
    }

    [RelayCommand]
    private void ShowTrackedSessions()
    {
        IsTrackedSessions = true;
        OnPropertyChanged(nameof(IsTrackedSessions));
    }

    partial void OnIsTrackedSessionsChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        Series = ActivityTimelineSeries.Build(_snapshots, _sessions, _acquiredUtc,
            LastPlayedUtc, _nowUtc, IsTrackedSessions);
        DetailText = Series.Summary;
    }
}
