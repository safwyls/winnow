namespace Winnow.Core.Ingest;

/// <summary>
/// Maps the id stored in <c>external_ids</c> to the id external services key on,
/// when the two differ (e.g. Epic CatalogItemId vs AppName). Implementations
/// read local store files; an empty map means "cannot say", not "no aliases".
/// </summary>
public interface IStoreArtifactAliasSource
{
    /// <summary>
    /// Returns stored-id-to-lookup-id aliases for <paramref name="provider"/>.
    /// Empty when this source has no data for the provider.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(
        string provider, CancellationToken ct = default);
}
