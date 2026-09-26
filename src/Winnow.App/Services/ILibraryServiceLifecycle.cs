namespace Winnow.App.Services;

public interface ILibraryServiceLifecycle
{
    Task RestartAsync(CancellationToken ct = default);
}
