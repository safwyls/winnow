namespace Winnow.App.Services;

public interface IFirstRunSetup
{
    string? StartupProblem { get; }
    Task<int?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(int? step, CancellationToken ct = default);
}
