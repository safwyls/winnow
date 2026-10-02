using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Identity;
using Winnow.App.Services;
using Winnow.Application.Identity;
using Winnow.Core.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class MergeCompositionParityTests
{
    [Fact]
    public async Task Production_merge_composition_loads_all_empty_review_sources_and_refreshes_through_HTTP()
    {
        var directory = NewDirectory();
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            try
            {
                Assert.NotNull(app.Services.GetRequiredService<IdentityReviewApplication>());
                Assert.NotNull(app.Services.GetRequiredService<PluginSyncService>());
                var refresh = Assert.IsType<MergeSuggestionRefresh>(app.Services.GetRequiredService<IMergeSuggestionRefresh>());
                using var scope = app.Services.CreateScope();
                Assert.Same(refresh, scope.ServiceProvider.GetRequiredService<IMergeSuggestionRefresh>());

                using var api = WinnowApiClient.Attach(directory);
                var initial = await api.GetAsync<IdentityReviewResponse>("identity/review");
                EmptyReview(initial);
                Assert.False(initial.HasCompletedSweep);
                Assert.Equal(0, refresh.Revision);

                var result = await api.SendAsync<object, SoftMatchSweepReport>(HttpMethod.Post,
                    "identity/review/refresh", new { });
                Assert.Equal(0, result.Outcome.Queued);
                Assert.Equal(0, result.Outcome.AutoMerged);
                Assert.Equal(1, refresh.Revision);
                var loaded = await api.GetAsync<IdentityReviewResponse>("identity/review");
                EmptyReview(loaded);
                Assert.True(loaded.HasCompletedSweep);
            }
            finally { await app.StopAsync(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Omitting_identity_links_from_production_composition_fails_review_resolution_naming_the_repository()
    {
        var directory = NewDirectory();
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.RemoveAll<IIdentityLinkRepository>());
            // The source guard resolves the screen from DI; it does not start a host.
            var error = Assert.Throws<InvalidOperationException>(
                app.Services.GetRequiredService<IdentityReviewApplication>);
            Assert.Contains(nameof(IIdentityLinkRepository), error.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void EmptyReview(IdentityReviewResponse review)
    {
        Assert.False(string.IsNullOrWhiteSpace(review.Revision));
        Assert.Empty(review.Candidates);
        Assert.Empty(review.History);
        Assert.Empty(review.Acts);
        Assert.Empty(review.Expansions);
        Assert.Empty(review.Workspace.Works);
        Assert.Empty(review.Workspace.Releases);
        Assert.Empty(review.Workspace.Ownerships);
        Assert.Empty(review.Workspace.Buckets);
        Assert.Empty(review.Workspace.IdentityLinks);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-merge-composition-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
