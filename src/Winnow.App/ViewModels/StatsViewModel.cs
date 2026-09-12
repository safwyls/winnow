using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>Navigation and asynchronous reads belong to one presentation surface.</summary>
public partial class StatsViewModel : ObservableObject, IDisposable
{
    public AccountStatsViewModel Spending { get; }
    public GameplayStatsViewModel Gameplay { get; }
    private bool _active;
    private long _generation;
    public StatsViewModel(AccountStatsViewModel spending, GameplayStatsViewModel gameplay)
    { Spending = spending; Gameplay = gameplay; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameplaySectionName), nameof(SpendingSectionName))]
    public partial bool IsSpending { get; set; }
    public string GameplaySectionName => IsSpending ? "Gameplay" : "Gameplay, selected";
    public string SpendingSectionName => IsSpending ? "Spending, selected" : "Spending";
    [ObservableProperty] public partial bool IsSpendingLoading { get; set; }
    [ObservableProperty] public partial string? SpendingProblem { get; set; }
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    partial void OnIsSpendingChanged(bool value)
    {
        if (!_active) return;
        Gameplay.Deactivate(); Spending.RefreshCommand.Cancel();
        _ = ActivateAsync();
    }
    public Task ActivateAsync()
    {
        _active = true;
        return PendingRefresh = IsSpending ? RefreshSpendingAsync() : Gameplay.ActivateAsync();
    }
    public void Deactivate() { _active = false; ++_generation; Gameplay.Deactivate(); Spending.RefreshCommand.Cancel(); IsSpendingLoading = false; }
    [RelayCommand] private void ShowGameplay() => IsSpending = false;
    [RelayCommand] private void ShowSpending() => IsSpending = true;
    [RelayCommand] public async Task RefreshSpendingAsync()
    {
        var generation = ++_generation;
        IsSpendingLoading = true; SpendingProblem = null;
        try { await Spending.RefreshCommand.ExecuteAsync(null); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (generation == _generation) SpendingProblem = "Couldn't read Steam spending. Try again."; }
        finally { if (generation == _generation) IsSpendingLoading = false; }
    }
    public void Dispose() { Deactivate(); Gameplay.Dispose(); GC.SuppressFinalize(this); }
}
