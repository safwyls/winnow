using Hoard.Core.Repositories;
using Hoard.Data;
using Hoard.Data.Repositories;
using Hoard.Resolve;
using Hoard.Resolve.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The composition test M1 did not have, and the reason §5.3 step 2 shipped
/// dead: <see cref="SoftMatchResolver"/> and <see cref="SoftMatcher"/> were
/// built, tested and correct, and registered in no container anywhere, so no
/// production code path could reach them. A type with no registration and no
/// caller passes every unit test it has.
///
/// <para>These build the container the way <c>Program.ConfigureServices</c>
/// does and then actually resolve the sweep, so the registration is exercised
/// rather than asserted about.</para>
/// </summary>
public sealed class SoftMatchRegistrationTests
{
    private static ServiceProvider Build(TempDatabase db)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        // The part Program already has.
        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<IUnitOfWorkFactory>(sp => (IUnitOfWorkFactory)sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();

        // The two lines this change adds.
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();
        services.AddSoftMatching();

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task The_sweep_resolves_from_the_container_and_runs()
    {
        using var db = new TempDatabase();
        using var provider = Build(db);

        var sweep = provider.GetRequiredService<LibrarySoftMatchSweep>();
        var report = await sweep.SweepAsync();

        Assert.Equal(0, report.Releases);
        Assert.NotNull(
            await provider.GetRequiredService<IResolveStateRepository>().GetLastSoftMatchSweepAsync());
    }

    [Fact]
    public void The_matcher_and_the_resolver_are_reachable_and_shared()
    {
        using var db = new TempDatabase();
        using var provider = Build(db);

        var resolver = provider.GetRequiredService<SoftMatchResolver>();

        Assert.Same(resolver, provider.GetRequiredService<SoftMatchResolver>());
        Assert.Same(provider.GetRequiredService<SoftMatcher>(), resolver.Matcher);
        Assert.Same(
            provider.GetRequiredService<SoftMatchThresholds>(),
            provider.GetRequiredService<SoftMatcher>().Thresholds);
    }

    /// <summary>
    /// Every registration is <c>TryAdd</c>, so retuned thresholds registered
    /// beforehand win. Tuning must not require editing the extension.
    /// </summary>
    [Fact]
    public void Registrations_defer_to_anything_already_in_the_container()
    {
        using var db = new TempDatabase();
        var tuned = new SoftMatchThresholds { QueueFloor = 0.9, PriorityThreshold = 0.95 };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(tuned);
        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<IUnitOfWorkFactory>(sp => (IUnitOfWorkFactory)sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();
        services.AddSoftMatching();

        using var provider = services.BuildServiceProvider();

        Assert.Same(tuned, provider.GetRequiredService<SoftMatcher>().Thresholds);
    }
}
