using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Application.Library;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddWinnowApplication(this IServiceCollection services, string databasePath, bool pooling = true)
    {
        services.TryAddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory(databasePath, pooling));
        services.TryAddSingleton<IUnitOfWorkFactory>(p => p.GetRequiredService<ISqliteConnectionFactory>());
        services.TryAddSingleton<DatabaseInitializer>();
        services.TryAddSingleton<ILibraryQueryRepository, LibraryQueryRepository>();
        services.TryAddSingleton<IWorkRepository, WorkRepository>();
        services.TryAddSingleton<IGameListRepository, GameListRepository>();
        services.TryAddSingleton<IHiddenGameRepository, HiddenGameRepository>();
        services.TryAddSingleton<IManualEntryRepository, ManualEntryRepository>();
        services.TryAddSingleton<IIdentityLinkRepository, IdentityLinkRepository>();
        services.TryAddSingleton<ISettingsRepository, SettingsRepository>();
        services.TryAddSingleton<IFacetRepository, FacetRepository>();
        services.TryAddSingleton<IWorkIgdbPinRepository, WorkIgdbPinRepository>();
        services.TryAddSingleton<IGroupHeaderPreferenceRepository, GroupHeaderPreferenceRepository>();
        services.TryAddSingleton<IArtworkChoiceRepository, ArtworkChoiceRepository>();
        services.TryAddSingleton<IStorefrontRepository, Winnow.Enrich.Stores.StorefrontCache>();
        services.TryAddSingleton<Winnow.App.Services.IEpicLaunchKeys, Winnow.App.Services.SqliteEpicLaunchKeys>();
        services.TryAddSingleton<ILibraryApplication, LibraryApplication>();
        return services;
    }

    public static void InitializeWinnowDatabase(this IServiceProvider services)
        => services.GetRequiredService<DatabaseInitializer>().Initialize();
}
