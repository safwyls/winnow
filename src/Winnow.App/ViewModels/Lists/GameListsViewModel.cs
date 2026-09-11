using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// The LISTS section of the details modal: one row per hand-built list, ticked
/// when the game is already a member. Live lists are excluded — a live list
/// holds a rule and finds its own members, so there is nothing to tick.
///
/// <para>Membership is resolved per game, not per store entry: the repository
/// resolves <c>same_game</c> identity links in SQL, so a game owned on two
/// stores shows one answer. <c>expansion_of</c> links are excluded — an
/// expansion's membership is its own.</para>
/// </summary>
public partial class GameListsViewModel : ObservableObject
{
    public GameListsViewModel(IReadOnlyList<GameListEntryViewModel> rows) => Rows = rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLists), nameof(IsEmpty))]
    public partial IReadOnlyList<GameListEntryViewModel> Rows { get; private set; }

    internal void ApplySnapshot(IReadOnlyList<GameListEntryViewModel> rows)
    {
        var existing = Rows.ToDictionary(row => row.List.Id);
        Rows = rows.Select(row =>
        {
            if (!existing.TryGetValue(row.List.Id, out var current)) return row;
            current.ApplySnapshot(row);
            return current;
        }).ToArray();
    }

    public string Heading => GameListsCopy.Heading;

    public bool HasLists => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => GameListsCopy.EmptyText;
}

/// <summary>
/// One checkbox row in the modal's LISTS section. Ticking appends the tile's
/// primary release. Unticking removes every release the membership rows name,
/// which is the only way to leave a list you joined from a different store's
/// copy of the game.
/// </summary>
public partial class GameListEntryViewModel : ObservableObject
{
    private Func<GameListEntryViewModel, bool, Task> _toggle;

    /// <summary>
    /// Guards against re-entrant toggles: the constructor sets
    /// <see cref="IsMember"/> to seed the checkbox, and the write-back to
    /// the repository must not fire for that initial assignment.
    /// </summary>
    private bool _applying;
    private bool _committed;

    public GameListEntryViewModel(
        GameListViewModel list,
        IReadOnlyList<long> memberReleaseIds,
        Func<GameListEntryViewModel, bool, Task> toggle)
    {
        List = list;
        MemberReleaseIds = memberReleaseIds;
        _toggle = toggle;

        _applying = true;
        _committed = IsMember = memberReleaseIds.Count > 0;
        _applying = false;
    }

    public GameListViewModel List { get; }

    public string Name => List.Name;

    public IReadOnlyList<long> MemberReleaseIds { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName), nameof(SelectionLabel))]
    public partial bool IsMember { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasStatus))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasStatus))]
    public partial string? Problem { get; private set; }

    public string SelectionLabel => $"{(IsMember ? "✓ " : "")}{Name}";
    public string? StatusText => IsBusy ? GameListsCopy.Saving : Problem;
    public bool HasStatus => StatusText is { Length: > 0 };

    public string AutomationName => IsMember
        ? GameListsCopy.RemoveAutomationName(Name)
        : GameListsCopy.AddAutomationName(Name);

    public Task Pending { get; private set; } = Task.CompletedTask;

    public void RecordMembership(IReadOnlyList<long> memberReleaseIds)
        => MemberReleaseIds = memberReleaseIds;

    internal void ApplySnapshot(GameListEntryViewModel row)
    {
        if (IsBusy) return;
        _toggle = row._toggle;
        MemberReleaseIds = row.MemberReleaseIds;
        _applying = true;
        try { _committed = IsMember = row.IsMember; }
        finally { _applying = false; }
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnIsMemberChanged(bool value)
    {
        if (_applying || IsBusy)
        {
            return;
        }

        Pending = ToggleAsync();
    }

    private async Task ToggleAsync()
    {
        IsBusy = true;
        Problem = null;
        try
        {
            while (IsMember != _committed)
            {
                var wanted = IsMember;
                await _toggle(this, wanted);
                _committed = wanted;
            }
        }
        catch (Exception)
        {
            _applying = true;
            try { IsMember = _committed; }
            finally { _applying = false; }
            Problem = GameListsCopy.SaveFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
