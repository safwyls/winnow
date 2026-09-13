using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

public sealed record StatsStoreOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Surface-local scope and range over the library's already-filtered ownerships.</summary>
public partial class GameplayStatsViewModel : ObservableObject, IDisposable
{
    // Keep custom history bounded and long charts readable without discarding covered dates.
    private const int MaximumRangeDays = 3660;
    private const int EarliestCalendarYear = 1900;
    private const int WeeklyRangeDays = 90;
    private const int TargetLongRangeBars = 26;
    private readonly IGameplayStatsRepository _repository;
    private readonly LibraryViewModel _library;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private CancellationTokenSource? _read;
    private long _generation;
    private bool _active, _disposed, _rebuildingStores;
    public GameplayStatsViewModel(IGameplayStatsRepository repository, LibraryViewModel library,
        TimeProvider? clock = null, TimeZoneInfo? timeZone = null)
    {
        _repository = repository; _library = library; _clock = clock ?? TimeProvider.System; _zone = timeZone ?? TimeZoneInfo.Local;
        var today = LocalNow.Date;
        CustomFrom = today.AddDays(-29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CustomUntil = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _library.TilesChanged += LibraryChanged;
        RebuildStores();
    }
    private DateTime LocalNow => TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _zone).DateTime;
    public IReadOnlyList<string> PeriodOptions { get; } = ["30 days", "90 days", "Custom"];
    [ObservableProperty] public partial IReadOnlyList<StatsStoreOption> StoreOptions { get; set; } = [];
    [ObservableProperty] public partial StatsStoreOption SelectedStore { get; set; } = new("", "All stores");
    [ObservableProperty] public partial string SelectedPeriod { get; set; } = "30 days";
    [ObservableProperty] public partial string CustomFrom { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomUntil { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasData { get; set; }
    [ObservableProperty] public partial string? Problem { get; set; }
    [ObservableProperty] public partial string PeriodLabel { get; set; } = string.Empty;
    [ObservableProperty] public partial string HoursText { get; set; } = "0 h";
    [ObservableProperty] public partial string GamesText { get; set; } = "0";
    [ObservableProperty] public partial string MedianText { get; set; } = "No completed sessions";
    [ObservableProperty] public partial string SessionNote { get; set; } = string.Empty;
    [ObservableProperty] public partial string LibraryNote { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> HoursChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> GamesChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> LengthChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> LibraryChart { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<AccountChartItem> StoresChart { get; set; } = [];
    [ObservableProperty] public partial int DashboardVersion { get; set; }
    public bool IsCustom => SelectedPeriod == "Custom";
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    public string CoverageNote => "Completed Winnow sessions on this library, not lifetime playtime or active attention. Overlapping games count independently. Account filters select games, not who played.";
    partial void OnSelectedStoreChanged(StatsStoreOption value) { if (!_rebuildingStores) ScopeChanged(); }
    partial void OnSelectedPeriodChanged(string value) { OnPropertyChanged(nameof(IsCustom)); ScopeChanged(); }
    private void ScopeChanged() { if (_active) _ = RefreshAsync(); }
    private void LibraryChanged(object? sender, EventArgs e) { RebuildStores(); ScopeChanged(); }
    private void RebuildStores()
    {
        var selectedKey = SelectedStore?.Key ?? string.Empty;
        _rebuildingStores = true;
        StoreOptions = new[] { new StatsStoreOption("", "All stores") }.Concat(_library.AllTiles.SelectMany(x => x.Entries)
            .Select(x => x.Store).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new StatsStoreOption(x, StoreNaming.Label(x)))).ToArray();
        SelectedStore = StoreOptions.FirstOrDefault(x => x.Key == selectedKey) ?? StoreOptions[0];
        _rebuildingStores = false;
    }
    public Task ActivateAsync() { _active = true; return RefreshAsync(); }
    public void Deactivate() { _active = false; CancelPending(); }
    [RelayCommand] private void Cancel()
    {
        CancelPending(); Problem = "Reading stopped. Choose Try again to resume."; DashboardVersion++;
    }
    private void CancelPending()
    {
        ++_generation; _read?.Cancel(); _read?.Dispose(); _read = null; IsLoading = false;
    }
    [RelayCommand] private Task ApplyDatesAsync() => RefreshAsync();
    [RelayCommand] public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        CancelPending();
        Problem = null; HasData = false;
        if (!TryRange(out var from, out var until)) { DashboardVersion++; return Task.CompletedTask; }
        var scope = _library.AllTiles.ToArray();
        var store = SelectedStore.Key;
        var visible = scope.Where(tile => tile.Entries.Any(entry => store.Length == 0 || entry.Store == store)).ToArray();
        var ownerships = scope.SelectMany(tile => tile.Entries.Select(entry => new GameplayOwnershipScope(entry.OwnershipId, tile.Game.ResolvedWorkId))).DistinctBy(x => x.OwnershipId).ToArray();
        var fromUtc = Boundary(from); var untilUtc = Boundary(until.AddDays(1));
        var bins = new List<GameplayTimeBin>();
        var span = (until - from).Days + 1;
        var step = span <= WeeklyRangeDays ? 7 : Math.Max(7, (int)Math.Ceiling(span / (double)TargetLongRangeBars));
        for (var date = from; date <= until; date = date.AddDays(step))
            bins.Add(new(Boundary(date), Boundary(date.AddDays(step) > until.AddDays(1) ? until.AddDays(1) : date.AddDays(step))));
        var request = new GameplayStatsRequest { Ownerships = ownerships, FromUtc = fromUtc, UntilUtc = untilUtc,
            AsOfUtc = _clock.GetUtcNow().UtcDateTime, Store = store.Length == 0 ? null : store, TimeBins = bins };
        PeriodLabel = $"{SelectedStore.Label} · {from:d MMM yyyy} – {until:d MMM yyyy} · local dates";
        LibraryChart = visible.GroupBy(x => x.Bucket).OrderByDescending(x => x.Count()).Select(x =>
            new AccountChartItem(BucketLabel(x.Key), x.Count(), x.Count().ToString("N0"), "Azure")).ToArray();
        StoresChart = visible.SelectMany(x => x.Entries.Where(e => store.Length == 0 || e.Store == store))
            .DistinctBy(x => x.OwnershipId).GroupBy(x => x.Store).OrderByDescending(x => x.Count())
            .Select(x => new AccountChartItem(StoreNaming.Label(x.Key), x.Count(), x.Count().ToString("N0"), "TextDim")).ToArray();
        LibraryNote = $"{visible.Length:N0} {(visible.Length == 1 ? "game" : "games")} you own {(store.Length == 0 ? "across these stores" : "here")}, grouped by current library status. Status includes linked store copies; changing the dates does not change these counts. Never played means no recorded play; Retired does not mean completed.";
        var cancellation = _read = new CancellationTokenSource();
        var generation = ++_generation;
        IsLoading = true; DashboardVersion++;
        return PendingRefresh = ReadAsync(request, scope, generation, cancellation.Token);
    }
    private async Task ReadAsync(GameplayStatsRequest request, IReadOnlyList<GameTileViewModel> scope, long generation, CancellationToken ct)
    {
        try
        {
            var result = await Task.Run(() => _repository.GetAsync(request, ct), ct);
            if (_disposed || ct.IsCancellationRequested || generation != _generation) return;
            HoursText = Hours(result.RecordedSeconds); GamesText = result.GamesPlayedCount.ToString("N0");
            MedianText = result.MedianSessionSeconds is { } seconds ? Duration(seconds) : "No completed sessions";
            SessionNote = $"{result.StartedSessionCount:N0} completed sessions started in this period; median and bands use their full lengths. Hours include the portions of {result.OverlappingSessionCount:N0} sessions within these dates. {result.ExcludedSessionCount:N0} unfinished or invalid records excluded.";
            HoursChart = result.Periods.Select(x => new AccountChartItem(
                $"{Local(x.FromUtc):d MMM} – {Local(x.UntilUtc.AddTicks(-1)):d MMM}", (decimal)x.RecordedSeconds, Hours(x.RecordedSeconds), "Azure")).ToArray();
            var names = scope.DistinctBy(x => x.Game.ResolvedWorkId).ToDictionary(x => x.Game.ResolvedWorkId, x => x.Title);
            GamesChart = result.TopGames.Select(x => new AccountChartItem(names.GetValueOrDefault(x.ResolvedWorkId, "Game no longer in view"), (decimal)x.RecordedSeconds, Hours(x.RecordedSeconds), "Azure")).ToArray();
            LengthChart = result.SessionLengths.Select(x => new AccountChartItem(LengthLabel(x.MinimumSeconds), x.Count, x.Count.ToString("N0"), "TextDim")).ToArray();
            HasData = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed && generation == _generation) Problem = "Couldn't read gameplay statistics. Try again."; }
        finally { if (!_disposed && generation == _generation) { IsLoading = false; DashboardVersion++; } }
    }
    private bool TryRange(out DateTime from, out DateTime until)
    {
        until = LocalNow.Date; from = until.AddDays(SelectedPeriod == "90 days" ? -89 : -29);
        if (!IsCustom) return true;
        if (!DateTime.TryParseExact(CustomFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out from)
            || !DateTime.TryParseExact(CustomUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out until))
        { Problem = "Enter both dates as YYYY-MM-DD."; return false; }
        if (from > until) { Problem = "The start date must be on or before the end date."; return false; }
        if (until > LocalNow.Date) { Problem = "Choose an end date no later than today."; return false; }
        if ((until - from).Days > MaximumRangeDays || from.Year < EarliestCalendarYear) { Problem = "Choose a period of up to ten years, starting in 1900 or later."; return false; }
        return true;
    }
    private DateTime Boundary(DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (_zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return _zone.IsAmbiguousTime(local) ? new DateTimeOffset(local, _zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime : TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }
    private DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
    private static string Hours(double seconds) => seconds is > 0 and < 360 ? "<0.1 h" : (seconds / 3600d).ToString("0.#", CultureInfo.CurrentCulture) + " h";
    private static string Duration(double seconds) => seconds < 3600 ? (seconds / 60).ToString("0.#", CultureInfo.CurrentCulture) + " min" : Hours(seconds);
    private static string LengthLabel(long min) => min switch { 0 => "Under 30 min", 1800 => "30–60 min", 3600 => "1–2 h", _ => "2 h or more" };
    private static string BucketLabel(string key) => key switch
    {
        LibraryBuckets.NeverPlayed => "Never played", LibraryBuckets.Active => "Active", LibraryBuckets.Bounced => "Started",
        LibraryBuckets.Retired => "Retired", LibraryBuckets.StaleButPatched => "Patched", LibraryBuckets.Derelict => "Derelict", _ => key
    };
    public void Dispose() { if (_disposed) return; _disposed = true; _library.TilesChanged -= LibraryChanged; CancelPending(); GC.SuppressFinalize(this); }
}

internal sealed class GameplayStatsUnavailableRepository : IGameplayStatsRepository
{
    public Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
        => Task.FromException<GameplayStats>(new InvalidOperationException("Gameplay statistics service is unavailable."));
}
