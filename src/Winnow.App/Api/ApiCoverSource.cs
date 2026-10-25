using Winnow.Api.Client;
using Winnow.Covers;

namespace Winnow.App.Api;

/// <summary>The backend resolves providers and credentials; this process only renders returned images.</summary>
public sealed class ApiCoverSource(WinnowApiClient api) : ICoverSource
{
    public string Name => "backend-v1";
    public bool CanHandle(CoverKey key) => true;
    public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        => api.GetArtworkAsync(key.Provider, key.Id, ct: ct);
}
