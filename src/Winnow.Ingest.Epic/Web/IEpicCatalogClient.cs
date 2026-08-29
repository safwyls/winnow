using Winnow.Ingest.Epic.Web.Model;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Asks Epic's catalog service for title and categories of owned catalog items.
/// Missing ids in the result mean "nothing learned"; never throws on failure.
/// </summary>
public interface IEpicCatalogClient
{
    /// <summary>What the catalog service says about each of <paramref name="catalogItemIds"/>.</summary>
    /// <param name="catalogItemIds">Catalog item ids to look up. Duplicates and blanks are ignored.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>> GetItemsAsync(
        IReadOnlyCollection<string> catalogItemIds, CancellationToken ct = default);
}
