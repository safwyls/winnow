namespace Winnow.App.ViewModels.Fullscreen;

/// <summary>Stable page and game identity, owned by the fullscreen presentation.</summary>
public sealed class FullscreenBrowseState
{
    public int PageSize { get; private set; } = 12;

    public void Resize(int pageSize, IReadOnlyList<long> releases)
    {
        PageSize = Math.Max(1, pageSize);
        Reconcile(releases);
    }
    public int Page { get; private set; }
    public long? SelectedReleaseId { get; private set; }
    public int PositionOnPage { get; private set; }

    public int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);

    public void Select(long releaseId, int position)
    {
        SelectedReleaseId = releaseId;
        PositionOnPage = Math.Clamp(position, 0, PageSize - 1);
    }

    public void Reconcile(IReadOnlyList<long> releases)
    {
        var index = SelectedReleaseId is { } selected ? IndexOf(releases, selected) : -1;
        if (index >= 0)
        {
            Page = index / PageSize;
            PositionOnPage = index % PageSize;
        }
        else
        {
            Page = Math.Clamp(Page, 0, PageCount(releases.Count) - 1);
            PositionOnPage = Math.Clamp(PositionOnPage, 0, Math.Max(0, Math.Min(PageSize, releases.Count - Page * PageSize) - 1));
            SelectedReleaseId = releases.Count > 0 ? releases[Page * PageSize + PositionOnPage] : null;
        }
    }

    public bool MovePage(int delta, IReadOnlyList<long> releases)
    {
        var page = Math.Clamp(Page + delta, 0, PageCount(releases.Count) - 1);
        if (page == Page) return false;
        Page = page;
        PositionOnPage = Math.Min(PositionOnPage, releases.Count - Page * PageSize - 1);
        SelectedReleaseId = releases[Page * PageSize + PositionOnPage];
        return true;
    }

    public bool MoveGridEdge(int delta, int columns, IReadOnlyList<long> releases)
    {
        var column = PositionOnPage % columns;
        if (!MovePage(delta, releases)) return false;
        var count = Math.Min(PageSize, releases.Count - Page * PageSize);
        var row = delta > 0 ? 0 : (count - 1) / columns;
        PositionOnPage = Math.Min(row * columns + column, count - 1);
        SelectedReleaseId = releases[Page * PageSize + PositionOnPage];
        return true;
    }

    private static int IndexOf(IReadOnlyList<long> releases, long release)
    {
        for (var i = 0; i < releases.Count; i++)
            if (releases[i] == release) return i;
        return -1;
    }
}
