using System.Net.Http;
using System.Reflection;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Gog;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// F04/F49. <see cref="ILocalLibrarySync"/> promises no network call, and the
/// snapshot scheduler runs it every fifteen minutes on that promise. These
/// tests are the enforcement: a maintainer who adds an HTTP-backed dependency
/// to the local job — which is exactly how the old <c>SteamSyncService</c>
/// acquired two network APIs behind a "filesystem-only" doc comment — fails
/// here rather than in a user's blank window.
/// </summary>
public sealed class LocalLibrarySyncContractTests
{
    /// <summary>
    /// Namespaces whose types exist to talk to a remote service. Anything from
    /// one of these in the local job's dependency closure is a regression, even
    /// if the type itself holds no <see cref="HttpClient"/>.
    /// </summary>
    private static readonly string[] NetworkNamespaces =
    [
        "Winnow.Enrich",
        "Winnow.Covers.Igdb",
        "Winnow.Ingest.Epic.Web",
        "System.Net",
    ];

    /// <summary>
    /// The strongest available form: build the local job's real dependency
    /// graph in a container that registers no HTTP client at all. If anything
    /// in the closure needed one, resolution throws.
    /// </summary>
    [Fact]
    public void The_local_job_resolves_from_a_container_with_no_http_registrations()
    {
        using var db = new TempDatabase();
        var services = new ServiceCollection();

        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<IUnitOfWorkFactory>(sp => sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<IWorkRepository, Winnow.Data.Repositories.WorkRepository>();
        services.AddSingleton<IReleaseRepository, Winnow.Data.Repositories.ReleaseRepository>();
        services.AddSingleton<IOwnershipRepository, Winnow.Data.Repositories.OwnershipRepository>();
        services.AddSingleton<IPlayRecordRepository, Winnow.Data.Repositories.PlayRecordRepository>();
        services.AddSingleton<IPlaytimeSnapshotRepository, Winnow.Data.Repositories.PlaytimeSnapshotRepository>();
        services.AddSingleton<LibraryFoldersReader>();
        services.AddSingleton<AppManifestReader>();
        services.AddSingleton<LocalConfigReader>();
        services.AddSingleton<SteamAccountEnumerator>();
        services.AddSingleton<SteamLibrarySource>();
        services.AddEpicIngest();
        services.AddGogIngest();
        services.AddSingleton<ExternalIdResolver>();
        services.AddSingleton<LibrarySyncGate>();
        services.AddSingleton<LocalLibrarySyncService>();
        services.AddSingleton<ILocalLibrarySync>(sp => sp.GetRequiredService<LocalLibrarySyncService>());
        services.AddLogging(b => b.ClearProviders());

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<LocalLibrarySyncService>(provider.GetRequiredService<ILocalLibrarySync>());

        // No IHttpClientFactory was ever registered, so the graph above closed
        // without one. Stated as an assertion so the omission is deliberate
        // rather than an accident of this list's length.
        Assert.Null(services.FirstOrDefault(d =>
            d.ServiceType.Name.Contains("HttpClient", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The container test only proves the graph closes; this walks it. Every
    /// constructor parameter and every field, transitively, must be free of
    /// <see cref="HttpClient"/> and of the namespaces that exist to reach a
    /// remote service.
    /// </summary>
    [Fact]
    public void No_type_in_the_local_jobs_constructor_closure_can_reach_the_network()
    {
        var offenders = new List<string>();
        Walk(typeof(LocalLibrarySyncService), [], offenders);

        Assert.Empty(offenders);
    }

    /// <summary>The remote job is the control: the same walk must find offenders there.</summary>
    [Fact]
    public void The_same_walk_does_find_the_network_in_the_remote_job()
    {
        var offenders = new List<string>();
        Walk(typeof(RemoteOwnershipSyncService), [], offenders);

        Assert.NotEmpty(offenders);
    }

    /// <summary>
    /// The scheduler's own contract. It is typed to the local job, so no
    /// registration mistake can put the network back on a fifteen-minute timer.
    /// </summary>
    [Fact]
    public void The_snapshot_scheduler_takes_the_local_job_and_only_the_local_job()
    {
        var parameters = typeof(SnapshotSchedulerService)
            .GetConstructors()
            .Single()
            .GetParameters();

        Assert.Contains(parameters, p => p.ParameterType == typeof(ILocalLibrarySync));
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(IRemoteOwnershipSync));
        Assert.False(typeof(ILocalLibrarySync).IsAssignableFrom(typeof(RemoteOwnershipSyncService)));
    }

    private static void Walk(Type type, HashSet<Type> seen, List<string> offenders)
    {
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, seen, offenders);
            }

            type = type.GetGenericTypeDefinition();
        }

        if (type.IsPrimitive || type == typeof(string) || !seen.Add(type))
        {
            return;
        }

        if (type == typeof(HttpClient) || type.Name.Contains("HttpClient", StringComparison.Ordinal))
        {
            offenders.Add(type.FullName ?? type.Name);
            return;
        }

        var ns = type.Namespace ?? string.Empty;
        if (NetworkNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal)))
        {
            offenders.Add(type.FullName ?? type.Name);
            return;
        }

        // Only Winnow's own types are worth descending into: the framework's
        // logging and time abstractions are the leaves of every graph here.
        if (!ns.StartsWith("Winnow", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var parameter in type.GetConstructors()
                     .SelectMany(c => c.GetParameters())
                     .Select(p => p.ParameterType))
        {
            Walk(parameter, seen, offenders);
        }

        foreach (var field in type
                     .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                     .Select(f => f.FieldType))
        {
            Walk(field, seen, offenders);
        }
    }
}
