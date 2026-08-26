using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.Core.Domain;
using Hoard.Core.Queries;

namespace Hoard.App.ViewModels.Lists;

/// <summary>
/// One list in the rail. Two kinds, and the difference is what the user has to
/// know: a <b>list</b> holds the games you put in it, a <b>live list</b> holds
/// the rules and finds the games again every time the library changes.
///
/// <para><b>They are told apart by which section of the rail they are in</b> —
/// <c>LISTS</c> and <c>LIVE LISTS</c> — rather than by a coloured mark. A second
/// coloured dot beside a count is the one thing the rail cannot afford: the
/// Flare pip on <c>Patched since</c> means unread, and a dot's meaning survives
/// exactly as long as there is only one of them (§5.2). A heading says the same
/// thing in words, scales to any number of lists, and is legible to a reader who
/// cannot resolve a 7px dot at all (§8).</para>
///
/// <para><b>Names are set in body type, not the rail's Display S caps.</b> The
/// buckets are the application's own vocabulary and are shouted; a list name is
/// the user's own sentence and is not.</para>
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

    /// <summary>
    /// Rule-backed. Stored in <c>lists.is_smart</c>, which is migration 0001's
    /// older word for the same flag; the interface never says "smart" — §7 names
    /// things by what the user controls, and what they control is whether the
    /// list keeps finding new members.
    /// </summary>
    public bool IsLive { get; }

    public bool IsManual => !IsLive;

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// Carried so a rename can round-trip it. The repository's rename replaces
    /// the description with exactly what it is given, null included, so a UI that
    /// forgot to pass it back would silently erase it.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>A live list's rules; <see cref="LibraryFilter.Empty"/> for a manual one.</summary>
    [ObservableProperty]
    public partial LibraryFilter Filter { get; set; }

    /// <summary>Release ids, in the order the user put them. Empty for a live list.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<long> ReleaseIds { get; set; } = [];

    /// <summary>
    /// How many titles are in it right now. Recomputed on every library load for
    /// both kinds — a manual list drops a count when one of its games is
    /// consolidated away, and a live list's number is the whole point.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Plex Mono, tabular, grouped — the same numeric column as the buckets.</summary>
    public string CountText => Count.ToString("N0");

    /// <summary>What the open list's header calls it. Uppercase, like every other kind label.</summary>
    public string KindLabel => IsLive ? "LIVE LIST" : "LIST";

    /// <summary>The one sentence a live list needs and a manual one does not.</summary>
    public string KindNote => IsLive
        ? "Updates itself as your library changes."
        : "The titles you put in it, in the order you put them.";
}
