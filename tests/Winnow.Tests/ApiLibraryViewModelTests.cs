using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Library;
using Winnow.Api.Contracts.Protocol;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Winnow.Tests;

public sealed class ApiLibraryViewModelTests
{
    [Fact]
    public async Task MetadataConflictPreservesObservedRevisionForReview()
    {
        var attempts = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new Winnow.Api.Contracts.Details.MetadataResponse(1, "Original", false, [], "seen"));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("seen", body.RootElement.GetProperty("expectedRevision").GetString());
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        }));
        using var api = new WinnowApiClient(Connection, http);
        var editor = new Winnow.App.Services.ApiWorkMetadataEditService(api);
        await editor.GetAsync(1);
        Assert.Equal(Winnow.Core.Domain.WorkFieldEditOutcome.Conflict, await editor.SetFieldAsync(1, "name", "Draft"));
        Assert.Equal(Winnow.Core.Domain.WorkFieldEditOutcome.Conflict, await editor.SetFieldAsync(1, "name", "Draft"));
        Assert.Equal(2, attempts);
        Assert.Contains("draft", GameMetadataEditorCopy.ProblemFor(Winnow.Core.Domain.WorkFieldEditOutcome.Conflict), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrontendDependencyGraphContainsNoBackendImplementationAssemblies()
    {
        string[] forbidden = ["Winnow.Data", "Winnow.Application", "Winnow.Ingest", "Winnow.Enrich", "Winnow.Monitor", "Winnow.Plugins", "Winnow.Resolve", "Winnow.Recommend"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<System.Reflection.Assembly>();
        pending.Enqueue(typeof(LibraryViewModel).Assembly);
        while (pending.TryDequeue(out var assembly))
        {
            if (!seen.Add(assembly.FullName!)) continue;
            foreach (var reference in assembly.GetReferencedAssemblies().Where(x => x.Name?.StartsWith("Winnow", StringComparison.Ordinal) == true))
            {
                Assert.DoesNotContain(forbidden, name => reference.Name == name || reference.Name!.StartsWith(name + ".", StringComparison.Ordinal));
                pending.Enqueue(System.Reflection.Assembly.Load(reference));
            }
        }
    }

    [Fact]
    public void FrontendCompositionBuildsBothSurfacesWithoutStorageOrWorkers()
    {
        using var api = new WinnowApiClient(Connection);
        var services = new ServiceCollection();
        services.AddLogging();
        var root = Path.Combine(Path.GetTempPath(), "winnow-frontend-composition", Guid.NewGuid().ToString("N"));
        Winnow.App.Api.FrontendServiceRegistration.AddWinnowFrontend(services,
            new Winnow.App.Services.DataLocation(root, Path.Combine(root, "winnow.db"), Winnow.App.Services.DataMigrationOutcome.None), api);
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<Winnow.Core.Repositories.ILibraryQueryRepository>());
        Assert.Null(provider.GetService<Winnow.Core.Repositories.IOwnershipRepository>());
        Assert.Null(provider.GetService<Winnow.Core.Repositories.IIdentityLinkRepository>());
        var shell = provider.GetRequiredService<MainWindowViewModel>();
        Assert.NotNull(shell.Library);
        Assert.NotNull(shell.MergeQueue);
        using var fullscreen = Winnow.App.Views.Fullscreen.FullscreenContext.Create(provider, shell);
        Assert.NotSame(shell.Library, fullscreen.Library);
    }

    [Fact]
    public async Task LibraryUsesBackendGroupingAndMetadataWithoutRepositories()
    {
        var game = new WorkspaceGameGrouping(1, Winnow.Core.Queries.LibraryBuckets.Bounced, 150,
            DateTime.UtcNow.AddYears(-2), null, 0, 2, null);
        var workspace = new LibraryWorkspaceResponse(new(false, false, "unrestricted"),
            [new(1, 11, 1, 1, 100, game.LastPlayedAt, null, game.Bucket, 0, null, game),
             new(2, 12, 1, 1, 50, game.LastPlayedAt, null, game.Bucket, 0, null, game)],
            [new() { Id = 1, Name = "Shared title", FirstReleaseYear = 2007, Summary = "Shared metadata" }],
            [new() { Id = 1, ReleaseId = 11, Store = "steam" }, new() { Id = 2, ReleaseId = 12, Store = "gog" }],
            [new() { Id = 11, WorkId = 1, Name = "Steam" }, new() { Id = 12, WorkId = 1, Name = "GOG" }],
            [], [], [], [], [], [], [], [], new Dictionary<string, EpicLaunchKeyResponse>(),
            new Dictionary<string, Winnow.Core.Repositories.StorefrontDetails>(), new Dictionary<long, string?>(), [],
            new Dictionary<long, PluginEntryActionResponse>());
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("/api/v1/library/workspace", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(workspace));
        }));
        using var api = new WinnowApiClient(Connection, http);
        using var vm = new LibraryViewModel(api: api);
        await vm.LoadCommand.ExecuteAsync(null);
        var tile = Assert.Single(vm.VisibleTiles);
        Assert.Equal("Shared title", tile.Title);
        Assert.Equal(150, tile.PlaytimeMinutes);
        Assert.Equal(game.Bucket, tile.Bucket);
        Assert.Equal(2, tile.Entries.Count);
        Assert.Equal(2007, tile.ReleaseYear);
    }

    [Fact]
    public async Task ListEditsUseDisplayedRevisionAndPreserveStateOnConflict()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new LibraryResponse([], [new(7, "Next", null, false, [1], "displayed-revision")]));
            Assert.Equal("/api/v1/lists/7", request.RequestUri!.AbsolutePath);
            var edit = await request.Content!.ReadFromJsonAsync<EditListRequest>();
            Assert.Equal("displayed-revision", edit!.ExpectedRevision);
            return new HttpResponseMessage(HttpStatusCode.Conflict);
        }));
        using var api = new WinnowApiClient(Connection, http);
        var vm = new ListsViewModel(api: api);
        await vm.LoadAsync();
        var list = Assert.Single(vm.Lists);
        await Assert.ThrowsAsync<BackendApiException>(() => vm.RenameAsync(list, "Changed elsewhere"));
        Assert.Equal("Next", list.Name);
        Assert.True(vm.HasProblem);
    }

    [Fact]
    public async Task SettingsLoadsRemoteHiddenAndManualEntriesWithoutRepositories()
    {
        var hiddenAt = DateTime.UtcNow.AddDays(-3);
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/hidden-games" => Json(new[] { new HiddenGameResponse(2, "Hidden", hiddenAt, 3) }),
            "/api/v1/manual-games" => Json(new[] { new ManualGameResponse(1, 2, 3, "Manual", null, null, 0, 2001, "PC", null, null, hiddenAt, hiddenAt) }),
            "/api/v1/library/visibility-counts" => Json(new Winnow.Api.Contracts.Details.VisibilityCountsResponse(0, 2, 0)),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
        })));
        using var api = new WinnowApiClient(Connection, http);
        var vm = new LibrarySettingsViewModel(api: api);
        await vm.RefreshAsync();
        Assert.Equal("Hidden", Assert.Single(vm.HiddenGames).Title);
        var manual = Assert.Single(vm.ManualEntries);
        Assert.Equal("Manual", manual.Title);
        Assert.Equal(hiddenAt, manual.Entry.AddedAt);
    }

    [Fact]
    public async Task RemoteManualIdentifierConflictRemainsAnInlineFieldError()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { field = "IgdbId", reason = "MappingChanged" })
        })));
        using var api = new WinnowApiClient(Connection, http);
        var vm = new LibrarySettingsViewModel(api: api) { DraftTitle = "Manual", DraftIgdbId = "42" };
        await vm.SaveFormCommand.ExecuteAsync(null);
        Assert.True(vm.HasIgdbIdError);
        Assert.Null(vm.Problem);
    }

    private static BackendDiscovery Connection => new("http://127.0.0.1:4567", "secret", "epoch", 1, "1");
    private static HttpResponseMessage Json<T>(T value) => new() { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
