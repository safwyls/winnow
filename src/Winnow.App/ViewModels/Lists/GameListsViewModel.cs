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

    public IReadOnlyList<GameListEntryViewModel> Rows { get; }

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
    private readonly Func<GameListEntryViewModel, bool, Task> _toggle;

    /// <summary>
    /// Guards against re-entrant toggles: the constructor sets
    /// <see cref="IsMember"/> to seed the checkbox, and the write-back to
    /// the repository must not fire for that initial assignment.
    /// </summary>
    private bool _writing;

    public GameListEntryViewModel(
        GameListViewModel list,
        IReadOnlyList<long> memberReleaseIds,
        Func<GameListEntryViewModel, bool, Task> toggle)
    {
        List = list;
        MemberReleaseIds = memberReleaseIds;
        _toggle = toggle;

        _writing = true;
        IsMember = memberReleaseIds.Count > 0;
        _writing = false;
    }

    public GameListViewModel List { get; }

    public string Name => List.Name;

    public IReadOnlyList<long> MemberReleaseIds { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial bool IsMember { get; set; }

    public string AutomationName => IsMember
        ? GameListsCopy.RemoveAutomationName(Name)
        : GameListsCopy.AddAutomationName(Name);

    public Task Pending { get; private set; } = Task.CompletedTask;

    public void RecordMembership(IReadOnlyList<long> memberReleaseIds)
        => MemberReleaseIds = memberReleaseIds;

    partial void OnIsMemberChanged(bool value)
    {
        if (_writing)
        {
            return;
        }

        Pending = ToggleAsync(value);
    }

    private async Task ToggleAsync(bool wanted)
    {
        _writing = true;
        try
        {
            await _toggle(this, wanted);
        }
        finally
        {
            _writing = false;
        }
    }
}
