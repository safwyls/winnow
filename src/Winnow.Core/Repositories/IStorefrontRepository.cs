namespace Winnow.Core.Repositories;

/// <summary>Stored storefront facts. Implementations never fetch while reading.</summary>
public interface IStorefrontRepository
{
    Task<IReadOnlyDictionary<string, StorefrontDetails>> ReadAllAsync(CancellationToken ct = default);
}

/// <summary>Verified store URL and readable patch notes from a cached product response.</summary>
public sealed record StorefrontDetails(string? StoreUrl, string? PatchNotes);
