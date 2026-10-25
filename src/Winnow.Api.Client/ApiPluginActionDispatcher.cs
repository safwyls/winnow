using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.Api.Client;

public sealed class ApiPluginActionDispatcher(WinnowApiClient api) : IPluginActionDispatcher
{
    public async Task<bool> ExecuteAsync(long ownershipId, GameLink action, CancellationToken ct = default)
    {
        if (action.Kind != GameLinkKind.Link || ownershipId <= 0) return false;
        var result = await api.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post,
            $"entries/{ownershipId}/actions", new(Guid.NewGuid(), GameActionKind.OpenStore), ct: ct);
        return result == LaunchDispatch.HandedOff;
    }
}
