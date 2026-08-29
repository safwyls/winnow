using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// One list in the rail. A manual list holds specific games; a live list holds
/// filter rules and recomputes its members whenever the library changes.
/// </summary>
public partial class GameListViewModel : ObservableObject
{
    public GameListViewModel(GameList record)
    {
        Id = record.Id;
        Name = record.Name;
        Description = record.Description;
        IsLive = record.IsLive;
        Filter = record.Filter;
    }

    public long Id { get; }

    /// <summary>Rule-backed list (stored as <c>lists.is_smart</c> in the DB).</summary>
    public bool IsLive { get; }

    public bool IsManual => !IsLive;

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Carried so a rename round-trips it (repository replaces with what it receives).</summary>
    public string? Description { get; set; }

    /// <summary>A live list's rules; <see cref="LibraryFilter.Empty"/> for a manual one.</summary>
    [ObservableProperty]
    public partial LibraryFilter Filter { get; set; }

    /// <summary>Release ids, in the order the user put them. Empty for a live list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<long> ReleaseIds { get; set; } = [];

    /// <summary>Current member count, recomputed on every library load.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Formatted count for display.</summary>
    public string CountText => Count.ToString("N0");

    /// <summary>What the open list's header calls it. Uppercase, like every other kind label.</summary>
    public string KindLabel => IsLive ? "LIVE LIST" : "LIST";

    /// <summary>The one sentence a live list needs and a manual one does not.</summary>
    public string KindNote => IsLive
        ? "Updates itself as your library changes."
        : "The titles you put in it, in the order you put them.";
}
