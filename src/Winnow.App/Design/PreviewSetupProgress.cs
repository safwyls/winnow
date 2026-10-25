namespace Winnow.App.Services;

internal sealed class PreviewSetupProgress : IFirstRunSetup
{
    private int? _step;
    public string? StartupProblem => null;
    public Task<int?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_step);
    public Task SaveAsync(int? step, CancellationToken ct = default) { _step = step; return Task.CompletedTask; }
}
