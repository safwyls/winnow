using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// Manages the rail's LISTS and LIVE LISTS sections and their persistence.
/// Repository is optional (graceful degradation when not registered).
/// Live lists are never materialised; their members come from <see cref="LibraryFilter"/> at draw time.
/// </summary>
public partial class ListsViewModel : ObservableObject
{
    private readonly IGameListRepository? _lists;

    public ListsViewModel(IGameListRepository? lists = null)
        => _lists = lists;

    /// <summary>Hand-built lists, alphabetical.</summary>
    public ObservableCollection<GameListViewModel> Lists { get; } = [];

    /// <summary>Rule-backed lists, alphabetical.</summary>
    public ObservableCollection<GameListViewModel> LiveLists { get; } = [];

    /// <summary>The list currently being shown in the grid, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListOpen), nameof(IsManualListOpen), nameof(IsLiveListOpen))]
    public partial GameListViewModel? Open { get; set; }

    public bool IsListOpen => Open is not null;

    public bool IsManualListOpen => Open is { IsManual: true };

    public bool IsLiveListOpen => Open is { IsLive: true };

    public bool HasLists => Lists.Count > 0;

    public bool HasLiveLists => LiveLists.Count > 0;

    public bool HasNoLists => Lists.Count == 0 && LiveLists.Count == 0;

    /// <summary>Show the LISTS header when there are manual lists or when both sections are empty.</summary>
    public bool ShowListsHeader => Lists.Count > 0 || HasNoLists;

    /// <summary>Empty-state guidance text.</summary>
    public const string EmptyMessage =
        "No lists yet. Select titles to create one, or save a filter as a live list.";

    /// <summary>Bindable accessor for <see cref="EmptyMessage"/>.</summary>
    public string EmptyMessageText => EmptyMessage;

    public IEnumerable<GameListViewModel> All => Lists.Concat(LiveLists);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var openId = Open?.Id;

        Lists.Clear();
        LiveLists.Clear();

        if (_lists is null)
        {
            Open = null;
            RaiseSectionState();
            return;
        }

        var records = await _lists.GetAllAsync(ct);
        foreach (var record in records.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var list = new GameListViewModel(record);
            if (list.IsLive)
            {
                LiveLists.Add(list);
            }
            else
            {
                var items = await _lists.GetItemsAsync(record.Id, ct);
                list.ReleaseIds = [.. items.Select(i => i.ReleaseId)];
                Lists.Add(list);
            }
        }

        // Re-find the open list by id after reload (row objects were replaced).
        Open = openId is { } id ? All.FirstOrDefault(l => l.Id == id) : null;
        foreach (var list in All)
        {
            list.IsSelected = ReferenceEquals(list, Open);
        }

        RaiseSectionState();
    }

    /// <summary>Creates a hand-built list seeded with the current selection.</summary>
    public async Task<GameListViewModel?> CreateListAsync(
        string name, IReadOnlyList<long> releaseIds, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (_lists is null || trimmed.Length == 0)
        {
            return null;
        }

        var id = await _lists.InsertAsync(GameList.Manual(trimmed), ct);
        foreach (var releaseId in releaseIds)
        {
            await _lists.AppendItemAsync(id, releaseId, ct);
        }

        var list = new GameListViewModel(GameList.Manual(trimmed) with { Id = id })
        {
            ReleaseIds = releaseIds,
        };

        Insert(Lists, list);
        RaiseSectionState();
        return list;
    }

    /// <summary>Creates a rule-backed list from the filter as it stands.</summary>
    public async Task<GameListViewModel?> CreateLiveListAsync(
        string name, LibraryFilter filter, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (_lists is null || trimmed.Length == 0)
        {
            return null;
        }

        var record = GameList.Live(trimmed, filter);
        var id = await _lists.InsertAsync(record, ct);

        var list = new GameListViewModel(record with { Id = id });
        Insert(LiveLists, list);
        RaiseSectionState();
        return list;
    }

    /// <summary>Adds releases to a manual list, keeping order and ignoring duplicates.</summary>
    public async Task AddToListAsync(
        GameListViewModel list, IEnumerable<long> releaseIds, CancellationToken ct = default)
    {
        if (list.IsLive)
        {
            return;
        }

        var next = list.ReleaseIds.ToList();
        foreach (var id in releaseIds)
        {
            if (next.Contains(id))
            {
                continue;
            }

            next.Add(id);

            if (_lists is not null)
            {
                await _lists.AppendItemAsync(list.Id, id, ct);
            }
        }

        list.ReleaseIds = next;
    }

    public async Task RemoveFromListAsync(
        GameListViewModel list, IEnumerable<long> releaseIds, CancellationToken ct = default)
    {
        if (list.IsLive)
        {
            return;
        }

        var dropped = releaseIds.ToHashSet();
        foreach (var id in dropped)
        {
            if (_lists is not null)
            {
                await _lists.RemoveItemAsync(list.Id, id, ct);
            }
        }

        list.ReleaseIds = [.. list.ReleaseIds.Where(id => !dropped.Contains(id))];
    }

    /// <summary>Moves one release by <paramref name="delta"/> places in a manual list.</summary>
    public async Task<bool> MoveAsync(
        GameListViewModel list, long releaseId, int delta, CancellationToken ct = default)
    {
        if (list.IsLive)
        {
            return false;
        }

        var order = list.ReleaseIds.ToList();
        var from = order.IndexOf(releaseId);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= order.Count)
        {
            return false;
        }

        order.RemoveAt(from);
        order.Insert(to, releaseId);
        list.ReleaseIds = order;

        if (_lists is not null)
        {
            await _lists.ReorderAsync(list.Id, order, ct);
        }

        return true;
    }

    public async Task RenameAsync(GameListViewModel list, string name, CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed == list.Name)
        {
            return;
        }

        list.Name = trimmed;

        if (_lists is not null)
        {
            // Pass description back unchanged to avoid erasing it.
            await _lists.RenameAsync(list.Id, trimmed, list.Description, ct);
        }

        Resort(list.IsLive ? LiveLists : Lists);
    }

    /// <summary>Updates a live list's filter rules in place.</summary>
    public async Task UpdateFilterAsync(
        GameListViewModel list, LibraryFilter filter, CancellationToken ct = default)
    {
        if (list.IsManual)
        {
            return;
        }

        list.Filter = filter;

        if (_lists is not null)
        {
            await _lists.SetFilterAsync(list.Id, filter, ct);
        }
    }

    /// <summary>Deletes the list (games themselves are not affected).</summary>
    public async Task DeleteAsync(GameListViewModel list, CancellationToken ct = default)
    {
        if (ReferenceEquals(Open, list))
        {
            Open = null;
        }

        (list.IsLive ? LiveLists : Lists).Remove(list);
        RaiseSectionState();

        if (_lists is not null)
        {
            await _lists.DeleteAsync(list.Id, ct);
        }
    }

    /// <summary>Rail selection. Exactly one row across both sections is ever marked.</summary>
    public void Select(GameListViewModel? list)
    {
        foreach (var candidate in All)
        {
            candidate.IsSelected = ReferenceEquals(candidate, list);
        }

        Open = list;
    }

    private static void Insert(ObservableCollection<GameListViewModel> into, GameListViewModel list)
    {
        var at = 0;
        while (at < into.Count
            && string.Compare(into[at].Name, list.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
        {
            at++;
        }

        into.Insert(at, list);
    }

    private static void Resort(ObservableCollection<GameListViewModel> collection)
    {
        var sorted = collection
            .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var at = collection.IndexOf(sorted[i]);
            if (at != i)
            {
                collection.Move(at, i);
            }
        }
    }

    private void RaiseSectionState()
    {
        OnPropertyChanged(nameof(HasLists));
        OnPropertyChanged(nameof(HasLiveLists));
        OnPropertyChanged(nameof(HasNoLists));
        OnPropertyChanged(nameof(ShowListsHeader));
    }
}
