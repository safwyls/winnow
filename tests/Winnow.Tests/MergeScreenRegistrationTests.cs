using Winnow.App.ViewModels;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Guards the container wiring for the Same Game screen. The original
/// risk was the opposite: <c>MergeExecutor</c> was registered and resolved
/// nowhere outside tests, so every confirmed pair sat unapplied while the
/// engine passed every test it had. That whole engine is gone (migration
/// 0019). What remains to guard is <c>IIdentityLinkRepository</c>, which
/// is a required constructor parameter precisely so an omission breaks the
/// container at startup instead of rendering a screen whose answers
/// quietly write nothing. These tests build the container the way
/// <c>Program.ConfigureServices</c> builds it and actually resolve the
/// screen and run its load, so the wiring is exercised rather than
/// asserted about.
/// </summary>
public sealed class MergeScreenRegistrationTests
{
    [Fact]
    public async Task The_screen_resolves_from_the_container_and_loads_every_list()
    {
        using var db = new TempDatabase();
        using var provider = Build(db, withLinks: true);

        var screen = provider.GetRequiredService<MergeQueueViewModel>();
        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Empty(screen.Groups);
        Assert.Empty(screen.ExpansionGroups);
        Assert.Empty(screen.LinkHistory);
        Assert.True(screen.ShowLinkHistoryEmpty);

        // And it opens on the queue, which is the surface the rail row counts.
        Assert.True(screen.IsReviewVisible);
    }

    /// <summary>
    /// The link repository is required rather than optional, so leaving
    /// it out of the composition root is a startup failure naming the
    /// type, not a screen that loads and writes nothing. This is the
    /// guard the old optional undo parameter could not offer.
    /// </summary>
    [Fact]
    public void Omitting_the_link_registration_breaks_the_container_by_name()
    {
        using var db = new TempDatabase();
        using var provider = Build(db, withLinks: false);

        var thrown = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<MergeQueueViewModel>);

        Assert.Contains("IIdentityLinkRepository", thrown.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(TempDatabase db, bool withLinks)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<IUnitOfWorkFactory>(
            sp => (IUnitOfWorkFactory)sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();
        services.AddSingleton<IOwnershipRepository, OwnershipRepository>();
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();

        // The expansion half of the screen. Required for the same reason the
        // link repository is: a scan the container cannot build is a segment
        // that renders an empty list forever.
        services.AddSingleton<IExpansionRefusalRepository, ExpansionRefusalRepository>();
        services.AddSoftMatching();

        if (withLinks)
        {
            services.AddSingleton<IIdentityLinkRepository, IdentityLinkRepository>();
        }

        services.AddMergeQueue();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
