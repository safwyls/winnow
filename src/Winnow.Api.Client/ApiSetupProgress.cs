using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;

namespace Winnow.Api.Client;

public sealed class ApiSetupProgress(WinnowApiClient api) : IFirstRunSetup
{
    public string? StartupProblem { get; private set; }
    public async Task<int?> LoadAsync(CancellationToken ct = default)
    {
        var progress = await api.GetAsync<SetupProgress>("setup", ct);
        StartupProblem = progress.Problem;
        return progress.Step;
    }

    public async Task SaveAsync(int? step, CancellationToken ct = default)
    {
        await api.SendAsync(HttpMethod.Put, "setup", new SetSetupProgress(step), ct);
        StartupProblem = null;
    }
}
