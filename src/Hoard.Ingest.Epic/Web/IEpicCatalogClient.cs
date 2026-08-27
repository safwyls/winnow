using Hoard.Ingest.Epic.Web.Model;

namespace Hoard.Ingest.Epic.Web;

/// <summary>
/// Asks Epic's catalog service what an owned catalog item actually is: its
/// title, and the categories that decide whether it belongs in a games library.
///
/// <para><b>Absence is the contract.</b> An id missing from the returned
/// dictionary means "nothing was learned about this item" and the caller must
/// leave every stored value for it exactly as it found it. Not signed in, no
/// credentials, a lapsed session, Epic unreachable, a 429 the retries could not
/// outlast, an unparseable body, an id the service does not recognise, and an id
/// whose namespace is unknown are all indistinguishable here — deliberately,
/// because the only safe reading of all of them is the same one.</para>
///
/// <para><b>Never throws for any of that.</b> §5.1: enrichment must degrade, not
/// fail.</para>
/// </summary>
public interface IEpicCatalogClient
{
    /// <summary>
    /// What the catalog service says about each of <paramref name="catalogItemIds"/>.
    ///
    /// <para>The service is keyed by <c>(namespace, catalogItemId)</c> and the
    /// caller supplies only the id, so this resolves the namespace itself from
    /// the account's owned library — the same cached fetch the ownership feed
    /// already makes. An id the library does not carry cannot be asked about and
    /// is simply absent from the result.</para>
    /// </summary>
    /// <param name="catalogItemIds">Catalog item ids to look up. Duplicates and blanks are ignored.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>> GetItemsAsync(
        IReadOnlyCollection<string> catalogItemIds, CancellationToken ct = default);
}
