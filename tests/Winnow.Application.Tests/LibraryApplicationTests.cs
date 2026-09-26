using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Winnow.Api.Contracts.Library;
using Winnow.Application;
using Winnow.Application.Library;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Application.Tests;

public sealed class LibraryApplicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-api-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly Changes _changes = new();
    private readonly ILibraryApplication _application;

    public LibraryApplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationChangePublisher>(_changes);
        services.AddWinnowApplication(Path.Combine(_directory, "winnow.db"), pooling: false);
        _services = services.BuildServiceProvider();
        _services.InitializeWinnowDatabase();
        _application = _services.GetRequiredService<ILibraryApplication>();
    }

    [Fact]
    public async Task ManualGameIsVisibleToIndependentApplicationClientAndHiddenAfterCommit()
    {
        var created = await _application.CreateManualGameAsync(new("A game", 2020));
        var independent = ActivatorUtilities.CreateInstance<LibraryApplication>(_services);
        var game = Assert.Single((await independent.GetLibraryAsync()).Games);
        Assert.Equal(created.WorkId, game.WorkId);
        Assert.Equal("never_played", game.Bucket);
        Assert.Equal("manual", Assert.Single(game.Entries).Store);
        await _application.SetHiddenAsync(new([created.WorkId], true));
        Assert.Empty((await independent.GetLibraryAsync()).Games);
        Assert.Equal(created.WorkId, Assert.Single(await independent.GetHiddenGamesAsync()).WorkId);
        await independent.SetHiddenAsync(new([created.WorkId], false));
        Assert.Single((await _application.GetLibraryAsync()).Games);
    }

    [Fact]
    public async Task ManualContentRevisionRejectsStaleEditsWithoutPublishing()
    {
        var original = await _application.CreateManualGameAsync(new("Original"));
        var firstEdit = await _application.UpdateManualGameAsync(original.OwnershipId,
            new("Changed", ExpectedIgdbMappingRevision: original.IgdbMappingRevision,
                ExpectedRevision: original.Revision));
        Assert.NotEqual(original.Revision, firstEdit.Revision);
        Assert.Equal(original.IgdbMappingRevision, firstEdit.IgdbMappingRevision);
        Assert.Contains(_changes.Events, x => x.Resource == $"games/{original.WorkId}");
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _application.UpdateManualGameAsync(original.OwnershipId,
                new("Stale", ExpectedIgdbMappingRevision: original.IgdbMappingRevision,
                    ExpectedRevision: original.Revision)));
        Assert.Equal("Changed", (await _application.GetManualGameAsync(original.OwnershipId))!.Title);
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task InvalidBulkHideRollsBackEntireOperationAndPublishesNothing()
    {
        var created = await _application.CreateManualGameAsync(new("A game"));
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationNotFoundException>(() =>
            _application.SetHiddenAsync(new([created.WorkId, long.MaxValue], true)));
        Assert.Single((await _application.GetLibraryAsync()).Games);
        Assert.Empty(await _application.GetHiddenGamesAsync());
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task StaleListEditCannotOverwriteAnotherClientOrPublishAChange()
    {
        var first = await _application.CreateManualGameAsync(new("First"));
        var second = await _application.CreateManualGameAsync(new("Second"));
        var list = await _application.CreateListAsync(new("Queue", [first.ReleaseId]));
        var updated = await _application.AddListMembersAsync(list.Id, new([second.ReleaseId], list.Revision));
        Assert.Equal(new[] { first.ReleaseId, second.ReleaseId }, updated.ReleaseIds);
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _application.EditListAsync(list.Id, new("Stale name", null, list.Revision)));
        var persisted = Assert.Single((await _application.GetLibraryAsync()).Lists);
        Assert.Equal("Queue", persisted.Name);
        Assert.Equal(updated.Revision, persisted.Revision);
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task FailedListCreationDoesNotLeaveAnEmptyList()
    {
        var entry = await _application.CreateManualGameAsync(new("First"));
        _changes.Events.Clear();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _application.CreateListAsync(new("Queue", [entry.ReleaseId, long.MaxValue])));
        Assert.Empty((await _application.GetLibraryAsync()).Lists);
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task IdentityCommandsUseExistingReversibleIdentityRules()
    {
        var parent = await _application.CreateManualGameAsync(new("Parent"));
        var child = await _application.CreateManualGameAsync(new("Child"));
        var act = await _application.LinkGamesAsync(new(parent.WorkId, [child.WorkId],
            new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [child.WorkId] = child.WorkId }));
        var grouped = Assert.Single((await _application.GetLibraryAsync()).Games);
        Assert.Equal(2, grouped.Entries.Count);
        Assert.Equal(parent.WorkId, (await _application.GetGameAsync(child.WorkId))!.WorkId);
        await _application.UndoIdentityActAsync(act.ActId);
        Assert.Equal(2, (await _application.GetLibraryAsync()).Games.Count);
    }

    [Fact]
    public async Task StaleSeparationCannotRetractReplacementIdentityLink()
    {
        var parent = await _application.CreateManualGameAsync(new("Parent"));
        var child = await _application.CreateManualGameAsync(new("Child"));
        var roots = new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [child.WorkId] = child.WorkId };
        await _application.LinkGamesAsync(new(parent.WorkId, [child.WorkId], roots));
        var originalLink = Assert.Single((await _application.GetWorkspaceAsync()).IdentityLinks);
        await _application.SeparateGameAsync(child.WorkId, new(originalLink.Id));
        await _application.LinkGamesAsync(new(parent.WorkId, [child.WorkId], roots));
        var replacement = Assert.Single((await _application.GetWorkspaceAsync()).IdentityLinks);
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _application.SeparateGameAsync(child.WorkId, new(originalLink.Id)));
        Assert.Equal(replacement.Id, Assert.Single((await _application.GetWorkspaceAsync()).IdentityLinks).Id);
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task WorkspaceSurvivesJsonRoundTripWithIdentityFacetsAndListRevision()
    {
        var parent = await _application.CreateManualGameAsync(new("Parent"));
        var child = await _application.CreateManualGameAsync(new("Child"));
        await _application.LinkGamesAsync(new(parent.WorkId, [child.WorkId],
            new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [child.WorkId] = child.WorkId }));
        var list = await _application.CreateListAsync(new("Queue", [child.ReleaseId]));
        await _services.GetRequiredService<IFacetRepository>().SetWorkFacetsAsync(parent.WorkId,
            [new Winnow.Core.Queries.FacetAssignment("genre", "Adventure")]);
        var workspace = await _application.GetWorkspaceAsync();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(workspace, options);
        var read = JsonSerializer.Deserialize<LibraryWorkspaceResponse>(json, options)!;
        Assert.Equal(2, read.Buckets.Count);
        Assert.All(read.Buckets, x => Assert.Equal(parent.WorkId, x.Game.ResolvedWorkId));
        Assert.Single(read.IdentityLinks);
        Assert.Contains(read.Facets, x => x.Kind == "genre" && x.Name == "Adventure");
        Assert.Equal(list.Revision, Assert.Single(read.ListVersions).Revision);
        Assert.Equal(child.ReleaseId, Assert.Single(read.ListItems).ReleaseId);
    }

    [Fact]
    public async Task LiveListFilterEditRequiresItsObservedRevision()
    {
        var list = await _application.CreateLiveListAsync(new("Installed", new() { Installed = true }));
        Assert.True(list.Filter!.Installed);
        var edited = await _application.SetListFilterAsync(list.Id,
            new(new() { Installed = false }, list.Revision));
        Assert.False(edited.Filter!.Installed);
        await Assert.ThrowsAsync<ApplicationConflictException>(() => _application.SetListFilterAsync(list.Id,
            new(new() { Installed = true }, list.Revision)));
    }

    [Fact]
    public async Task TypedPreferencesDoNotExposeOrOverwriteUnrelatedSecrets()
    {
        var settings = _services.GetRequiredService<ISettingsRepository>();
        await settings.SetAsync("credentials.test", "secret");
        var preferences = await _application.GetPreferencesAsync();
        await _application.SetPreferencesAsync(preferences with { ShowNonGameEntries = true });
        Assert.True((await _application.GetPreferencesAsync()).ShowNonGameEntries);
        Assert.Equal("secret", await settings.GetAsync("credentials.test"));
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class Changes : IApplicationChangePublisher
    {
        public List<(string Kind, string? Resource)> Events { get; } = [];
        public void Publish(string kind, string? resource = null) => Events.Add((kind, resource));
    }
}
