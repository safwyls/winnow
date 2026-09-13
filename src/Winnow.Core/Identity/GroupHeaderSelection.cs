namespace Winnow.Core.Identity;

/// <summary>Chooses header presentation without changing the group's identity or membership.</summary>
public static class GroupHeaderSelection
{
    public static long SelectWork(long rootWorkId, string? preferredStore,
        IEnumerable<(long WorkId, string Store)> available)
    {
        var members = available.Distinct().OrderBy(row => row.WorkId == rootWorkId ? 0 : 1)
            .ThenBy(row => row.WorkId).ToArray();
        return members.FirstOrDefault(row => row.Store == preferredStore).WorkId is > 0 and var preferred
            ? preferred : members.FirstOrDefault().WorkId is > 0 and var fallback ? fallback : rootWorkId;
    }
}
