using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>Approximate storefront evidence, kept outside the exact session projection.</summary>
public partial class SteamReportedActivityViewModel : ObservableObject, IDisposable
{
    private readonly ISteamPlaytimeObservationRepository? _repository;
    private readonly ISettingsRepository? _settings;
    private IReadOnlyDictionary<long, string> _scope;
    private CancellationTokenSource? _read;
    private int _revision;
    private bool _disposed;
    private IReadOnlyList<SteamReportedActivityRow> _all = [];
    private int _page;
    private const int PageSize = 20;

    public SteamReportedActivityViewModel(ISteamPlaytimeObservationRepository? repository = null,
        IReadOnlyDictionary<long, string>? scope = null, ISettingsRepository? settings = null)
    { _repository = repository; _scope = scope ?? new Dictionary<long, string>(); _settings = settings; }

    public const string Heading = "Steam-reported activity";
    public const string Explanation = "Approximate increases in Steam's playtime, observed between library checks. These are not exact sessions and are not added to recorded-session totals. Steam's lifetime total already includes this playtime.";
    public const string EmptyMessage = "No unexplained Steam activity found. New observations need time to settle before they appear.";
    [ObservableProperty] public partial IReadOnlyList<SteamReportedActivityRow> Entries { get; private set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = EmptyMessage;
    public bool HasStatus => Status.Length > 0;
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    public bool HasPrevious => _page > 0;
    public bool HasNext => (_page + 1) * PageSize < _all.Count;
    public string PageLabel => _all.Count == 0 ? "" : $"Page {_page + 1} of {(_all.Count + PageSize - 1) / PageSize}";
    public event EventHandler? Changed;

    public void UpdateScope(IReadOnlyDictionary<long, string> scope)
    {
        _scope = scope; _revision++; _read?.Cancel();
        IsLoading = false; Status = EmptyMessage;
        _all = []; _page = 0; Publish();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        var revision = ++_revision;
        _read?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _read = cancellation;
        var scope = _scope;
        IsLoading = true; Status = "Reading Steam-reported activity…";
        _all = []; _page = 0; Publish();
        try
        {
            if (_repository is null)
            { Status = "Steam-reported activity is unavailable."; return; }
            var ownOnly = _settings is not null && AccountScope.IsOwnOnly(await _settings.GetAsync(AccountScope.SettingKey, cancellation.Token));
            var account = ownOnly ? SteamOwnedAccount.Clean(await _settings!.GetAsync(SteamOwnedAccount.RefSettingKey, cancellation.Token)) : null;
            if (uint.TryParse(account, out var accountId)) account = accountId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_disposed || revision != _revision) return;
            if (ownOnly && account is null)
            { _all = []; Publish(); Status = "Confirm your Steam account in Settings to see its reported activity."; return; }
            var observations = await Task.Run(() => _repository.GetActivityAsync(scope.Keys.ToArray(),
                DateTime.UtcNow, account, cancellation.Token), cancellation.Token);
            if (_disposed || revision != _revision) return;
            _all = observations.Where(row => scope.ContainsKey(row.OwnershipId) && (account is null || row.AccountRef == account)
                    && (row.ComparisonUnavailable || row.UnexplainedMinutes > 0))
                .OrderByDescending(row => row.WindowEndedAt)
                .Select(row => new SteamReportedActivityRow(row, scope[row.OwnershipId])).ToArray();
            _page = Math.Min(_page, Math.Max(0, (_all.Count - 1) / PageSize));
            Status = _all.Count == 0 ? EmptyMessage : "";
            Publish();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        { if (!_disposed && revision == _revision) Status = "Couldn't read Steam-reported activity. Try again."; }
        finally
        {
            if (ReferenceEquals(_read, cancellation)) _read = null;
            if (!_disposed && revision == _revision) { IsLoading = false; Changed?.Invoke(this, EventArgs.Empty); }
        }
    }

    [RelayCommand] private void PreviousPage() { if (HasPrevious) { _page--; Publish(); } }
    [RelayCommand] private void NextPage() { if (HasNext) { _page++; Publish(); } }
    private void Publish()
    {
        Entries = _all.Skip(_page * PageSize).Take(PageSize).ToArray();
        OnPropertyChanged(nameof(HasPrevious)); OnPropertyChanged(nameof(HasNext)); OnPropertyChanged(nameof(PageLabel));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { _disposed = true; _revision++; _read?.Cancel(); }
}

public sealed class SteamReportedActivityRow(SteamReportedActivity activity, string gameTitle)
{
    public long OwnershipId => activity.OwnershipId;
    public string GameTitle => gameTitle;
    public string Duration => activity.ComparisonUnavailable
        ? $"About {activity.SteamDeltaMinutes:N0} min reported by Steam"
        : $"About {Math.Ceiling(activity.UnexplainedMinutes ?? 0):N0} min not matched to recorded sessions";
    public string ObservationBounds => $"Observed between {activity.WindowStartedAt.ToLocalTime():g} and {activity.WindowEndedAt.ToLocalTime():g}";
    public string Uncertainty => activity.ComparisonUnavailable
        ? "Shared accounts or an unfinished session prevent a reliable comparison. This increase may overlap recorded sessions; exact play times are unknown."
        : "Exact start and end times are unknown. This is an estimate from Steam's cumulative playtime.";
    public string AutomationName => $"{GameTitle}. {Duration}. {ObservationBounds}. {Uncertainty}";
}
