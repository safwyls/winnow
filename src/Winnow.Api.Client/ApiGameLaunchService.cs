using Winnow.Api.Contracts.Actions;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.Api.Client;

public sealed class ApiGameLaunchService(WinnowApiClient api) : IGameLaunchService
{
    public Task<LaunchDispatch> LaunchAsync(long ownershipId, GameLink action)
    {
        var kind = action.Kind switch
        {
            GameLinkKind.Play => GameActionKind.Play,
            GameLinkKind.Install => GameActionKind.Install,
            GameLinkKind.Uninstall => GameActionKind.Uninstall,
            _ => GameActionKind.Manage
        };
        return api.SendAsync<GameActionRequest, LaunchDispatch>(HttpMethod.Post,
            $"entries/{ownershipId}/actions", new(Guid.NewGuid(), kind));
    }
}
