using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

public interface IPluginFacetRepository
{
    Task SetAsync(long workId, string source, IReadOnlyList<FacetAssignment> facets, CancellationToken ct = default);
}
