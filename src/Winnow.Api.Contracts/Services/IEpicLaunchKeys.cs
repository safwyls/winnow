using Winnow.App.ViewModels;
namespace Winnow.App.Services;

public interface IEpicLaunchKeys
{
    Task<IReadOnlyDictionary<string, EpicLaunchKey>> GetAllAsync(CancellationToken ct = default);
}
