using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

public sealed class EnrichmentSettingsViewModel(
    IgdbSettingsViewModel igdb, PluginSettingsViewModel? plugins = null, ArtworkOrderViewModel? artworkOrder = null)
{
    public string Title => "Metadata & artwork";
    public string SegmentLabel => "METADATA & ARTWORK";
    public string SegmentTooltip => "Metadata credentials and background artwork sources";
    public string IntroMessage => "Connect metadata sources and choose which background artwork to try first.";
    public IgdbSettingsViewModel Igdb { get; } = igdb;
    public PluginSettingsViewModel Plugins { get; } = plugins ?? new();
    public ArtworkOrderViewModel ArtworkOrder { get; } = artworkOrder ?? new();
    public void ClearSecrets() { Igdb.ClientSecret = string.Empty; Plugins.ClearSecrets(); }
}

public partial class ArtworkOrderViewModel : ObservableObject
{
    private readonly ArtworkPreferences? _preferences;
    public const string Explanation = "Try sources from top to bottom. Your saved background always comes first. Standard Steam heroes and covers remain fallbacks.";
    public ObservableCollection<ArtworkSourcePreference> Sources { get; } = [];
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;

    public ArtworkOrderViewModel(ArtworkPreferences? preferences = null)
    {
        _preferences = preferences;
        Refresh();
        if (preferences is not null) preferences.Changed += () =>
        {
            if (Dispatcher.UIThread.CheckAccess()) Refresh();
            else Dispatcher.UIThread.Post(Refresh);
        };
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_preferences is not null) await _preferences.LoadAsync(ct);
        Refresh();
    }

    private bool CanMoveUp(string? source) => !IsBusy && IndexOf(source) > 0;
    private bool CanMoveDown(string? source) => !IsBusy && IndexOf(source) is var index && index >= 0 && index < Sources.Count - 1;
    private int IndexOf(string? source) => Sources.ToList().FindIndex(row => row.SourceId == source);
    [RelayCommand(CanExecute = nameof(CanMoveUp))] private Task MoveUpAsync(string? source) => MoveAsync(source, -1);
    [RelayCommand(CanExecute = nameof(CanMoveDown))] private Task MoveDownAsync(string? source) => MoveAsync(source, 1);

    private async Task MoveAsync(string? source, int offset)
    {
        var from = IndexOf(source);
        var to = from + offset;
        if (IsBusy || from < 0 || to < 0 || to >= Sources.Count) return;
        var order = Sources.Select(row => row.SourceId).ToList();
        (order[from], order[to]) = (order[to], order[from]);
        IsBusy = true;
        NotifyCommands();
        try
        {
            if (_preferences is not null) await _preferences.SaveAsync(order);
            Refresh(order);
            Status = "Artwork source order saved.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Could not save artwork source order. Check that Winnow's data folder is writable, then try again."; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private void Refresh() => Refresh(_preferences?.SourceOrder ?? [ArtworkPreferences.Steam, ArtworkPreferences.SteamGridDb, ArtworkPreferences.Igdb]);
    private void Refresh(IReadOnlyList<string> order)
    {
        Sources.Clear();
        foreach (var source in order) Sources.Add(new(source, SourceLabel(source), this));
        NotifyCommands();
    }
    private void NotifyCommands() { MoveUpCommand.NotifyCanExecuteChanged(); MoveDownCommand.NotifyCanExecuteChanged(); }

    private string SourceLabel(string source) => _preferences?.AvailableSources.FirstOrDefault(option => option.Id == source)?.Label
        ?? source switch
        {
            ArtworkPreferences.Steam => "High-resolution Steam heroes",
            ArtworkPreferences.SteamGridDb => "SteamGridDB",
            ArtworkPreferences.Igdb => "IGDB",
            _ => source
        };
}

public sealed class ArtworkSourcePreference(string sourceId, string label, ArtworkOrderViewModel owner)
{
    public string SourceId { get; } = sourceId;
    public string Label { get; } = label;
    public string MoveUpLabel => $"Move {Label} up";
    public string MoveDownLabel => $"Move {Label} down";
    public IAsyncRelayCommand<string?> MoveUpCommand => owner.MoveUpCommand;
    public IAsyncRelayCommand<string?> MoveDownCommand => owner.MoveDownCommand;
}
