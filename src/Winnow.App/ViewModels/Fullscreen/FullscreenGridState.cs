namespace Winnow.App.ViewModels.Fullscreen;

/// <summary>Stable game identity and a two-row viewport for fullscreen grids.</summary>
public sealed class FullscreenGridState
{
    private int _count;

    public int Columns { get; private set; } = 6;
    public int FirstRow { get; private set; }
    public int SelectedIndex { get; private set; }
    public long? SelectedReleaseId { get; private set; }
    public int PositionInView => SelectedIndex - FirstRow * Columns;
    public int Page => FirstRow / 2;

    public int PageCount(int count) => count <= 0 ? 1 : (RowCount(count) - 1) / 2 + 1;

    public void Resize(int columns, IReadOnlyList<long> releases)
    {
        Columns = Math.Max(1, columns);
        Reconcile(releases);
    }

    public void Reconcile(IReadOnlyList<long> releases)
    {
        _count = releases.Count;
        if (_count == 0)
        {
            SelectedIndex = 0;
            SelectedReleaseId = null;
            FirstRow = 0;
            return;
        }

        var index = SelectedReleaseId is { } selected ? IndexOf(releases, selected) : -1;
        SelectedIndex = index >= 0 ? index : Math.Clamp(SelectedIndex, 0, _count - 1);
        SelectedReleaseId = releases[SelectedIndex];
        RevealSelection();
    }

    public void Select(long releaseId, int absoluteIndex)
    {
        if (_count == 0) return;
        SelectedIndex = Math.Clamp(absoluteIndex, 0, _count - 1);
        SelectedReleaseId = releaseId;
        RevealSelection();
    }

    public bool MoveVertical(int delta, IReadOnlyList<long> releases) => MoveRows(delta, releases);

    public bool MovePage(int delta, IReadOnlyList<long> releases) => MoveRows((long)delta * 2, releases);

    private bool MoveRows(long delta, IReadOnlyList<long> releases)
    {
        Reconcile(releases);
        if (_count == 0) return false;
        var row = Math.Clamp(SelectedIndex / Columns + delta, 0, RowCount(_count) - 1);
        var index = (int)Math.Min(row * Columns + SelectedIndex % Columns, _count - 1);
        if (index == SelectedIndex) return false;
        Select(releases[index], index);
        return true;
    }

    private void RevealSelection()
    {
        var row = SelectedIndex / Columns;
        if (row < FirstRow) FirstRow = row;
        else if (row > FirstRow + 1) FirstRow = row - 1;
        FirstRow = Math.Clamp(FirstRow, 0, Math.Max(0, RowCount(_count) - 2));
    }

    private int RowCount(int count) => count <= 0 ? 0 : (count - 1) / Columns + 1;

    private static int IndexOf(IReadOnlyList<long> releases, long releaseId)
    {
        for (var i = 0; i < releases.Count; i++)
            if (releases[i] == releaseId) return i;
        return -1;
    }
}
