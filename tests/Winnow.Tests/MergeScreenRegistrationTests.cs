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
/// <c>MergeExecutor</c> and <c>IMergeExecutionRepository</c> were registered
/// in <c>Program</c> and resolved nowhere outside tests, so every confirmed
/// pair sat unapplied while the engine passed every test it had. A type with
/// a registration and no caller is indistinguishable from one that works.
///
/// <para>These build the container the way <c>Program.ConfigureServices</c>
/// builds it and then actually resolve the screen and run its load, so the
/// wiring is exercised rather than asserted about.</para>
/// </summary>
public sealed class MergeScreenRegistrationTests
{
    [Fact]
    public async Task The_screen_resolves_from_the_container_and_loads_every_list()
    {
        using var db = new TempDatabase();
        using var provider = Build(db, withUndo: true);

        var screen = provider.GetRequiredService<MergeQueueViewModel>();
        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Empty(screen.Groups);
        Assert.Empty(screen.Outstanding);
        Assert.Empty(screen.History);
        Assert.Empty(screen.LinkHistory);
        Assert.False(screen.HasOutstanding);
        Assert.True(screen.ShowLinkHistoryEmpty);

        // And it opens on the queue, which is the surface the rail row counts.
        Assert.True(screen.IsReviewVisible);
    }

    [Fact]
    public async Task The_executor_reaches_the_undo_repository_through_its_optional_parameter()
    {
        using var db = new TempDatabase();
        using var provider = Build(db, withUndo: true);

        // Resolving is not proof; asking for something only the undo repository
        // can answer is.
        Assert.Empty(await provider.GetRequiredService<MergeExecutor>().HistoryAsync());
    }

    /// <summary>
    /// The undo repository is an optional constructor parameter, which is how
    /// the data pass could land without holding Winnow.App. The cost of that is
    /// an omission the container cannot catch, so the wrappers must fail by name
    /// rather than by null reference.
    /// </summary>
    [Fact]
    public async Task Omitting_the_undo_registration_fails_by_name_rather_than_silently()
    {
        using var db = new TempDatabase();
        using var provider = Build(db, withUndo: false);

        var executor = provider.GetRequiredService<MergeExecutor>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.HistoryAsync());

        Assert.Contains("IMergeUndoRepository", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("MergeUndoRepository", thrown.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(TempDatabase db, bool withUndo)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        services.AddSingleton<IUnitOfWorkFactory>(
            sp => (IUnitOfWorkFactory)sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();
        services.AddSingleton<IMergeExecutionRepository, MergeExecutionRepository>();
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();
        services.AddSingleton<IIdentityLinkRepository, IdentityLinkRepository>();

        if (withUndo)
        {
            services.AddSingleton<IMergeUndoRepository, MergeUndoRepository>();
        }

        services.AddMergeExecution();
        services.AddMergeQueue();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
