using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly Winnow.Api.Client.WinnowApiClient? _api;
    private readonly Dictionary<long, string> _apiRevisions = [];
    private readonly SemaphoreSlim _writes = new(1, 1);
    internal long Revision { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    public bool HasProblem => Problem is { Length: > 0 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListsSectionState))]
    public partial bool AreListsExpanded { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveListsSectionState))]
    public partial bool AreLiveListsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCreateMenuOpen { get; set; }

    public string ListsSectionState => AreListsExpanded ? "Expanded" : "Collapsed";
    public string LiveListsSectionState => AreLiveListsExpanded ? "Expanded" : "Collapsed";

    [RelayCommand]
    private void ToggleLists() => AreListsExpanded = !AreListsExpanded;

    [RelayCommand]
    private void ToggleLiveLists() => AreLiveListsExpanded = !AreLiveListsExpanded;

    [RelayCommand]
    private void ToggleCreateMenu() => IsCreateMenuOpen = !IsCreateMenuOpen;

    internal event EventHandler? MembershipChanged;

    public ListsViewModel(IGameListRepository? lists = null, Winnow.Api.Client.WinnowApiClient? api = null)
    {
        _lists = lists;
        _api = api;
    }

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

    /// <summary>
    /// Whether the library is the screen on show. Mirrors
    /// <see cref="LibraryViewModel.IsCurrentScreen"/>: while the Feed or
    /// another screen is up, the open list's row keeps its underlying
    /// selection (<see cref="Open"/> is untouched) and drops the visible
    /// mark. Written by the shell whenever IsLibraryVisible changes.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCurrentScreen { get; set; } = true;

    public bool HasLists => Lists.Count > 0;

    public bool HasLiveLists => LiveLists.Count > 0;

    public bool HasNoLists => Lists.Count == 0 && LiveLists.Count == 0;

    /// <summary>Show the LISTS header when there are manual lists or when both sections are empty.</summary>
    public bool ShowListsHeader => Lists.Count > 0 || HasNoLists;

    /// <summary>Empty-state guidance text.</summary>
    public const string EmptyMessage =
        "No lists yet. Choose New list below to create a static or live list.";

    /// <summary>Bindable accessor for <see cref="EmptyMessage"/>.</summary>
    public string EmptyMessageText => EmptyMessage;

    public IEnumerable<GameListViewModel> All => Lists.Concat(LiveLists);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await WaitForWritesAsync(ct);
        var revision = Revision;
        if (_api is not null)
        {
            var response = await _api.GetLibraryAsync(ct);
            if (revision != Revision || IsBusy) { await LoadAsync(ct); return; }
            ApplyApiSnapshot(response.Lists);
            return;
        }
        var loaded = await Task.Run(async () =>
        {
            var records = _lists is null ? [] : await _lists.GetAllAsync(ct);
            var items = _lists is null ? [] : await _lists.GetAllItemsAsync(ct);
            return (records, items);
        }, ct);
        if (revision != Revision || IsBusy) { await LoadAsync(ct); return; }
        ApplySnapshot(loaded.records, loaded.items);
    }

    internal void ApplyApiSnapshot(IReadOnlyList<Winnow.Api.Contracts.Library.GameListResponse> lists)
    {
        _apiRevisions.Clear();
        foreach (var list in lists) _apiRevisions[list.Id] = list.Revision;
        ApplySnapshot(lists.Select(list => new GameList
        {
            Id = list.Id, Name = list.Name, Description = list.Description, IsSmart = list.IsLive,
            FilterJson = list.Filter?.ToJson()
        }).ToArray(), lists.SelectMany(list => list.ReleaseIds.Select((releaseId, index) =>
            new ListItem { ListId = list.Id, ReleaseId = releaseId, Position = index })).ToArray());
    }

    internal void ApplySnapshot(IReadOnlyList<GameList> records, IReadOnlyList<ListItem> items)
    {
        var openId = Open?.Id;
        var existing = All.ToDictionary(list => list.Id);

        Lists.Clear();
        LiveLists.Clear();

        if (_lists is null && _api is null)
        {
            Open = null;
            RaiseSectionState();
            return;
        }

        var itemsByList = items.ToLookup(item => item.ListId);
        foreach (var record in records.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var list = existing.TryGetValue(record.Id, out var current) && current.IsLive == record.IsLive
                ? current : new GameListViewModel(record);
            list.Name = record.Name;
            list.Description = record.Description;
            list.Filter = record.Filter;
            if (list.IsLive)
            {
                LiveLists.Add(list);
            }
            else
            {
                list.ReleaseIds = [.. itemsByList[record.Id].OrderBy(item => item.Position).Select(i => i.ReleaseId)];
                Lists.Add(list);
            }
        }

        // Preserve the open list while removing rows that no longer exist.
        Open = openId is { } id ? All.FirstOrDefault(l => l.Id == id) : null;
        MarkSelection();

        RaiseSectionState();
    }

    /// <summary>
    /// Which lists hold this game, resolved through <c>same_game</c> identity
    /// links in SQL. A list contains the game when any release of any work in
    /// the game's live link group is a member; <c>expansion_of</c> links are
    /// excluded, so an expansion's membership is its own.
    /// </summary>
    public async Task<IReadOnlyList<GameListMembership>> MembershipForGameAsync(
        long workId, CancellationToken ct = default)
        => _api is not null
            ? (await _api.GetAsync<Winnow.Api.Contracts.Details.GameDetailsResponse>($"games/{workId}/details", ct)).ListMemberships
            : _lists is null ? [] : await _lists.GetMembershipForGameAsync(workId, ct);

    /// <summary>Creates a hand-built list seeded with the current selection.</summary>
    public Task<GameListViewModel?> CreateListAsync(
        string name, IReadOnlyList<long> releaseIds, CancellationToken ct = default)
        => WriteAsync<GameListViewModel?>(async () =>
    {
        var trimmed = name.Trim();
        if ((_lists is null && _api is null) || trimmed.Length == 0)
        {
            return null;
        }

        var distinct = releaseIds.Distinct().ToArray();
        long id;
        if (_api is not null)
        {
            var created = await _api.CreateListAsync(new(trimmed, distinct), ct);
            id = created.Id;
            _apiRevisions[id] = created.Revision;
        }
        else id = await _lists!.CreateManualAsync(trimmed, distinct, ct);

        var list = new GameListViewModel(GameList.Manual(trimmed) with { Id = id })
        {
            ReleaseIds = distinct,
        };

        Insert(Lists, list);
        RaiseSectionState();
        MembershipChanged?.Invoke(this, EventArgs.Empty);
        return list;
    }, ct);

    /// <summary>Creates a rule-backed list from the filter as it stands.</summary>
    public Task<GameListViewModel?> CreateLiveListAsync(
        string name, LibraryFilter filter, CancellationToken ct = default)
        => WriteAsync<GameListViewModel?>(async () =>
    {
        var trimmed = name.Trim();
        if ((_lists is null && _api is null) || trimmed.Length == 0)
        {
            return null;
        }

        var record = GameList.Live(trimmed, filter);
        long id;
        if (_api is not null)
        {
            var result = await _api.CreateLiveListAsync(new(trimmed, filter), ct);
            id = result.Id;
            _apiRevisions[id] = result.Revision;
        }
        else id = await _lists!.InsertAsync(record, ct);

        var list = new GameListViewModel(record with { Id = id });
        Insert(LiveLists, list);
        RaiseSectionState();
        return list;
    }, ct);

    /// <summary>Adds releases to a manual list, keeping order and ignoring duplicates.</summary>
    public Task AddToListAsync(
        GameListViewModel list, IEnumerable<long> releaseIds, CancellationToken ct = default)
        => WriteAsync(async () =>
    {
        if (list.IsLive)
        {
            return false;
        }

        var requested = releaseIds.Distinct().ToArray();
        if (_api is not null)
        {
            var result = await _api.AddListMembersAsync(list.Id, new(requested, await GetApiRevisionAsync(list.Id, ct)), ct);
            _apiRevisions[list.Id] = result.Revision;
            list.ReleaseIds = result.ReleaseIds;
        }
        else list.ReleaseIds = _lists is null ? [.. list.ReleaseIds.Concat(requested).Distinct()]
            : await _lists.AppendItemsAsync(list.Id, requested, ct);
        MembershipChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }, ct);

    public Task RemoveFromListAsync(
        GameListViewModel list, IEnumerable<long> releaseIds, CancellationToken ct = default)
        => WriteAsync(async () =>
    {
        if (list.IsLive)
        {
            return false;
        }

        var dropped = releaseIds.ToHashSet();
        if (_api is not null)
        {
            var result = await _api.RemoveListMembersAsync(list.Id, new(dropped.ToArray(), await GetApiRevisionAsync(list.Id, ct)), ct);
            _apiRevisions[list.Id] = result.Revision;
            list.ReleaseIds = result.ReleaseIds;
        }
        else list.ReleaseIds = _lists is null ? [.. list.ReleaseIds.Where(id => !dropped.Contains(id))]
            : await _lists.RemoveItemsAsync(list.Id, dropped.ToArray(), ct);
        MembershipChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }, ct);

    /// <summary>Moves one release by <paramref name="delta"/> places in a manual list.</summary>
    public Task<bool> MoveAsync(
        GameListViewModel list, long releaseId, int delta, CancellationToken ct = default)
        => WriteAsync(async () =>
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
        if (_api is not null)
        {
            var result = await _api.ReorderListAsync(list.Id, new(order, await GetApiRevisionAsync(list.Id, ct)), ct);
            _apiRevisions[list.Id] = result.Revision;
            order = [.. result.ReleaseIds];
        }
        else if (_lists is not null)
        {
            order = [.. await _lists.ReorderAsync(list.Id, order, ct)];
        }

        list.ReleaseIds = order;
        return true;
    }, ct);

    public Task RenameAsync(GameListViewModel list, string name, CancellationToken ct = default)
        => WriteAsync(async () =>
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed == list.Name)
        {
            return false;
        }

        if (_api is not null)
        {
            var result = await _api.EditListAsync(list.Id, new(trimmed, list.Description, await GetApiRevisionAsync(list.Id, ct)), ct);
            _apiRevisions[list.Id] = result.Revision;
        }
        else if (_lists is not null)
        {
            // Pass description back unchanged to avoid erasing it.
            if (!await _lists.RenameAsync(list.Id, trimmed, list.Description, ct))
                throw new InvalidOperationException("The list is no longer available.");
        }

        list.Name = trimmed;
        Resort(list.IsLive ? LiveLists : Lists);
        return true;
    }, ct);

    /// <summary>Updates a live list's filter rules in place.</summary>
    public Task UpdateFilterAsync(
        GameListViewModel list, LibraryFilter filter, CancellationToken ct = default)
        => WriteAsync(async () =>
    {
        if (list.IsManual)
        {
            return false;
        }

        if (_api is not null)
        {
            var result = await _api.SetListFilterAsync(list.Id, new(filter, await GetApiRevisionAsync(list.Id, ct)), ct);
            _apiRevisions[list.Id] = result.Revision;
        }
        else if (_lists is not null)
        {
            if (!await _lists.SetFilterAsync(list.Id, filter, ct))
                throw new InvalidOperationException("The list is no longer available.");
        }
        list.Filter = filter;
        return true;
    }, ct);

    /// <summary>Deletes the list (games themselves are not affected).</summary>
    public Task DeleteAsync(GameListViewModel list, CancellationToken ct = default)
        => WriteAsync(async () =>
    {
        if (_api is not null)
        {
            await _api.DeleteListAsync(list.Id, await GetApiRevisionAsync(list.Id, ct), ct);
            _apiRevisions.Remove(list.Id);
        }
        else if (_lists is not null) await _lists.DeleteAsync(list.Id, ct);
        if (ReferenceEquals(Open, list))
        {
            Open = null;
        }

        (list.IsLive ? LiveLists : Lists).Remove(list);
        RaiseSectionState();

        return true;
    }, ct);

    internal async Task WaitForWritesAsync(CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        _writes.Release();
    }

    private Task<string> GetApiRevisionAsync(long listId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _apiRevisions.TryGetValue(listId, out var revision) ? Task.FromResult(revision)
            : throw new InvalidOperationException("Reload the list before editing it.");
    }

    private async Task<T> WriteAsync<T>(Func<Task<T>> write, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        Revision++;
        IsBusy = true;
        Problem = null;
        try { return await write(); }
        catch (Winnow.Api.Client.BackendApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        { Problem = GameListsCopy.Conflict; throw; }
        catch { Problem = GameListsCopy.SaveFailed; throw; }
        finally { Revision++; IsBusy = false; _writes.Release(); }
    }

    /// <summary>Rail selection. Exactly one row across both sections is ever marked.</summary>
    public void Select(GameListViewModel? list)
    {
        Open = list;
        MarkSelection();
    }

    partial void OnIsCurrentScreenChanged(bool value)
    {
        _ = value;
        MarkSelection();
    }

    /// <summary>Applies the visible mark, gated on <see cref="IsCurrentScreen"/>; <see cref="Open"/> itself is untouched.</summary>
    private void MarkSelection()
    {
        foreach (var candidate in All)
        {
            candidate.IsSelected = IsCurrentScreen && ReferenceEquals(candidate, Open);
        }
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
