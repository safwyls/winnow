using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;

namespace Winnow.Application.Details;

public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddWinnowImports(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton<IOwnershipRepository, OwnershipRepository>();
        services.TryAddSingleton<IReleaseRepository, ReleaseRepository>();
        services.TryAddSingleton<IAccountFactRepository, AccountFactRepository>();
        services.TryAddSingleton<LibrarySyncGate>();
        services.AddSteamAccountPageImport();
        services.TryAddSingleton<IAcquisitionExport, AcquisitionExport>();
        services.TryAddSingleton<ImportApplication>();
        return services;
    }
}
