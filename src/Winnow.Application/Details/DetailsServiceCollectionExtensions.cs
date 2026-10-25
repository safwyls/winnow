using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;

namespace Winnow.Application.Details;

public static class DetailsServiceCollectionExtensions
{
    public static IServiceCollection AddWinnowDetails(this IServiceCollection services)
    {
        services.TryAddSingleton<IUpdateEventRepository, UpdateEventRepository>();
        services.TryAddSingleton<IUpdateAcknowledgementRepository, UpdateAcknowledgementRepository>();
        services.TryAddSingleton<IPlaytimeSnapshotRepository, PlaytimeSnapshotRepository>();
        services.TryAddSingleton<ISessionRepository, SessionRepository>();
        services.TryAddSingleton<IWorkRatingRepository, WorkRatingRepository>();
        services.TryAddSingleton<IWorkImageRepository, WorkImageRepository>();
        services.TryAddSingleton<IAccountAcquisitionRepository, AccountAcquisitionRepository>();
        services.TryAddSingleton<IAccountAcquisitionReader, AccountAcquisitionReader>();
        services.TryAddSingleton<IAchievementQueryRepository, AchievementQueryRepository>();
        services.TryAddSingleton<IWorkFieldSourceRepository, WorkFieldSourceRepository>();
        services.TryAddSingleton<IWorkMetadataEditService, WorkMetadataEditService>();
        services.TryAddSingleton<IUpdateFlagService, UpdateFlagService>();
        services.TryAddSingleton<IActivityRepository, ActivityRepository>();
        services.TryAddSingleton<IGameplayStatsRepository, GameplayStatsRepository>();
        services.TryAddSingleton<IAccountStatsRepository, AccountStatsRepository>();
        services.TryAddSingleton<ILifecycleRepository, LifecycleRepository>();
        services.TryAddSingleton<ISteamPlaytimeObservationRepository, SteamPlaytimeObservationRepository>();
        services.TryAddSingleton<IDetailsApplication, DetailsApplication>();
        services.TryAddSingleton<SessionJournalService>();
        services.AddHostedService<DetailsJournalBridge>();
        return services;
    }
}
