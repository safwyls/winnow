using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Details;
using Winnow.Application;
using Winnow.Application.Details;
using Winnow.Application.Library;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Application.Tests;

public sealed class DetailsApplicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-details-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly Changes _changes = new();
    private readonly ILibraryApplication _library;
    private readonly IDetailsApplication _details;

    public DetailsApplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationChangePublisher>(_changes);
        services.AddWinnowApplication(Path.Combine(_directory, "winnow.db"), pooling: false);
        services.AddWinnowDetails();
        _services = services.BuildServiceProvider();
        _services.InitializeWinnowDatabase();
        _library = _services.GetRequiredService<ILibraryApplication>();
        _details = _services.GetRequiredService<IDetailsApplication>();
    }

    [Fact]
    public async Task DetailsPreservePerReleaseAchievementsAndPerOwnershipJournal()
    {
        var first = await _library.CreateManualGameAsync(new("First"));
        var second = await _library.CreateManualGameAsync(new("Second"));
        await _library.LinkGamesAsync(new(first.WorkId, [second.WorkId],
            new Dictionary<long, long> { [first.WorkId] = first.WorkId, [second.WorkId] = second.WorkId }));
        var sessionId = await SessionAsync(second.OwnershipId);
        var note = await _details.GetJournalAsync(sessionId);
        await _details.SaveJournalAsync(sessionId, new("An evening", 4, note.Revision));
        var details = await _details.GetDetailsAsync(second.WorkId);
        Assert.Equal(first.WorkId, details.WorkId);
        Assert.Equal(2, details.Ownerships.Count);
        Assert.Equal(2, details.Sessions.Count);
        Assert.Equal(sessionId, Assert.Single(details.Sessions[second.OwnershipId]).Id);
        Assert.Single(details.JournalEntries);
        Assert.Equal(2, details.Achievements.Count);
    }

    [Fact]
    public async Task StaleJournalWriteDoesNotOverwriteSavedNoteOrPublish()
    {
        var game = await _library.CreateManualGameAsync(new("First"));
        var sessionId = await SessionAsync(game.OwnershipId);
        var original = await _details.GetJournalAsync(sessionId);
        await _details.SaveJournalAsync(sessionId, new("Saved", 4, original.Revision));
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _details.SaveJournalAsync(sessionId, new("Stale", 2, original.Revision)));
        Assert.Equal("Saved", (await _details.GetJournalAsync(sessionId)).Note);
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task StaleIgdbDialogCannotAssignOrClearNewerPin()
    {
        var game = await _library.CreateManualGameAsync(new("First"));
        var observed = await _details.GetIgdbStateAsync(game.WorkId);
        await _services.GetRequiredService<IWorkIgdbPinRepository>().PinAsync(new()
        {
            WorkId = game.WorkId, IgdbId = 12345, Name = "Newer match"
        });
        var newer = await _details.GetIgdbStateAsync(game.WorkId);
        Assert.NotEqual(observed.Revision, newer.Revision);
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _details.AssignIgdbAsync(game.WorkId, new(54321, observed.Revision)));
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _details.ClearIgdbAsync(game.WorkId, new(observed.Revision)));
        Assert.Equal(newer, await _details.GetIgdbStateAsync(game.WorkId));
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task InvalidJournalRatingPublishesNothingAndLeavesNoteUnchanged()
    {
        var game = await _library.CreateManualGameAsync(new("First"));
        var sessionId = await SessionAsync(game.OwnershipId);
        var original = await _details.GetJournalAsync(sessionId);
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _details.SaveJournalAsync(sessionId, new("Invalid rating", 6, original.Revision)));
        Assert.Equal(original, await _details.GetJournalAsync(sessionId));
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task MetadataEditsRejectStaleStateAndPreserveProvenance()
    {
        var game = await _library.CreateManualGameAsync(new("First"));
        var original = await _details.GetMetadataAsync(game.WorkId);
        var outcome = await _details.SetMetadataAsync(game.WorkId, new("summary", "My summary", original.Revision));
        Assert.Equal("Applied", outcome.Outcome);
        _changes.Events.Clear();
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            _details.SetMetadataAsync(game.WorkId, new("summary", "Old draft", original.Revision)));
        var current = await _details.GetMetadataAsync(game.WorkId);
        Assert.Contains(current.Fields, x => x.Field == "summary" && x.Value == "My summary" && x.Source == "user");
        Assert.Empty(_changes.Events);
    }

    [Fact]
    public async Task DismissAcknowledgesOnlyObservedUpdates()
    {
        var game = await _library.CreateManualGameAsync(new("First"));
        var updates = _services.GetRequiredService<IUpdateEventRepository>();
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstPush = await updates.InsertAsync(new() { ReleaseId = game.ReleaseId, Kind = "build_push", OccurredAt = at });
        var firstNews = await updates.InsertAsync(new() { ReleaseId = game.ReleaseId, Kind = "announcement", OccurredAt = at });
        await updates.InsertAsync(new() { ReleaseId = game.ReleaseId, Kind = "build_push", OccurredAt = at.AddMonths(1) });
        await updates.InsertAsync(new() { ReleaseId = game.ReleaseId, Kind = "announcement", OccurredAt = at.AddMonths(1) });
        var result = await _details.AcknowledgeUpdatesAsync(game.ReleaseId, new([firstPush, firstNews]));
        Assert.Equal("Stored", result.Result);
        Assert.Equal(at, result.AcknowledgedThrough);
        Assert.Equal(at, (await _details.GetDetailsAsync(game.WorkId)).Acknowledgements[game.ReleaseId]);
    }

    private Task<long> SessionAsync(long ownershipId)
        => _services.GetRequiredService<ISessionRepository>().InsertAsync(new Session
        {
            OwnershipId = ownershipId, StartedAt = DateTime.UtcNow.AddHours(-1), EndedAt = DateTime.UtcNow,
            DurationSeconds = 3600, DetectionMethod = "manual",
        });

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
    private sealed class Changes : IApplicationChangePublisher
    {
        public List<string> Events { get; } = [];
        public void Publish(string kind, string? resource = null) => Events.Add(kind);
    }
}
