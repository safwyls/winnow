using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>Shares hero observations through the current game group without copying them into its root.</summary>
public static class BackdropImages
{
    public static async Task<IReadOnlyList<WorkImages>> LoadAsync(IWorkImageRepository repository,
        long primaryWorkId, IEnumerable<long> memberWorkIds, CancellationToken ct = default)
    {
        var rows = (await repository.GetForWorkAsync(primaryWorkId, ct)).ToList();
        foreach (var member in memberWorkIds.Where(id => id != primaryWorkId).Distinct())
            rows.AddRange((await repository.GetForWorkAsync(member, ct))
                .Where(row => (row.Source == ImageSources.SteamGridDb || row.Source.StartsWith("plugin:", StringComparison.Ordinal))
                    && row.Kind == ImageKinds.Artwork));
        return rows;
    }
}
