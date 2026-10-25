using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class JournalPromptParityTests
{
    [Fact]
    public async Task A_fresh_backend_keeps_prompts_off_until_explicit_opt_in()
    {
        await using var host = await Host.Start();
        Assert.False((await host.Client.GetAsync<JournalPreferences>("journal/preferences")).PromptAfterPlay);
        Assert.False(host.Application.Services.GetRequiredService<SessionJournalService>().PromptEnabled);
        await host.Client.SendAsync(HttpMethod.Put, "journal/preferences", new JournalPreferences(true));
        Assert.True((await host.Client.GetAsync<JournalPreferences>("journal/preferences")).PromptAfterPlay);
        Assert.True(host.Application.Services.GetRequiredService<SessionJournalService>().PromptEnabled);
        await host.Client.SendAsync(HttpMethod.Put, "journal/preferences", new JournalPreferences(false));
        Assert.False((await host.Client.GetAsync<JournalPreferences>("journal/preferences")).PromptAfterPlay);
        Assert.Null(await host.Sessions.GetNoteAsync(host.SessionId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_a_completed_sitting_exposes_its_exact_identity_and_duration(bool finished)
    {
        await using var host = await Host.Start(finished);
        if (finished)
        {
            var prompt = await host.Client.GetAsync<SessionPromptResponse>($"sessions/{host.SessionId}/prompt");
            Assert.Equal(host.SessionId, prompt.SessionId);
            Assert.Equal(host.OwnershipId, prompt.OwnershipId);
            Assert.Equal(47 * 60, prompt.DurationSeconds);
        }
        else
        {
            var error = await Assert.ThrowsAsync<BackendApiException>(() =>
                host.Client.GetAsync<SessionPromptResponse>($"sessions/{host.SessionId}/prompt"));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        }
        Assert.Null(await host.Sessions.GetNoteAsync(host.SessionId));
    }

    [Fact]
    public async Task A_refused_save_leaves_no_note_and_the_same_revision_can_retry()
    {
        await using var host = await Host.Start();
        var initial = await host.Read();
        host.Sessions.FailNext = true;
        await Assert.ThrowsAsync<BackendApiException>(() => host.Save("so close to the end", 5, initial.Revision));
        Assert.Null(await host.Sessions.GetNoteAsync(host.SessionId));
        Assert.Equal(initial.Revision, (await host.Read()).Revision);
        var saved = await host.Save("so close to the end", 5, initial.Revision);
        Assert.Equal("so close to the end", saved.Note);
        Assert.Equal(5, saved.Rating);
        Assert.NotEqual(initial.Revision, saved.Revision);
        Assert.Equal(2, host.Sessions.WriteAttempts);
    }

    [Fact]
    public async Task Graceful_backend_shutdown_waits_for_an_accepted_note_to_commit()
    {
        await using var host = await Host.Start();
        var initial = await host.Read();
        host.Sessions.GateWrites = true;
        var save = host.Save("shutdown mid-write", 3, initial.Revision);
        await host.Sessions.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stopping = host.Application.StopAsync();
        try
        {
            Assert.False(stopping.IsCompleted);
            Assert.Null(await host.Sessions.GetNoteAsync(host.SessionId));
        }
        finally { host.Sessions.Release.TrySetResult(); }
        var response = await save.WaitAsync(TimeSpan.FromSeconds(10));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("shutdown mid-write", response.Note);
        var persisted = await host.Sessions.GetNoteAsync(host.SessionId);
        Assert.Equal("shutdown mid-write", persisted!.Note);
        Assert.Equal(3, persisted.Rating);
    }

    [Fact]
    public async Task Closing_the_frontend_after_acceptance_does_not_abandon_its_note()
    {
        await using var host = await Host.Start();
        var initial = await host.Read();
        using var observer = WinnowApiClient.Attach(host.Directory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var events = observer.WatchEventsAsync(ct: deadline.Token).GetAsyncEnumerator(deadline.Token);
        Assert.True(await events.MoveNextAsync());
        host.Sessions.GateWrites = true;
        using var connection = new CancellationTokenSource();
        var save = host.Save("closed window mid-write", 4, initial.Revision, connection.Token);
        await host.Sessions.Entered.Task.WaitAsync(deadline.Token);
        await connection.CancelAsync();
        await host.Sessions.Disconnected.Task.WaitAsync(deadline.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        host.Sessions.Release.TrySetResult();
        Assert.True(await host.Sessions.Written.Task.WaitAsync(deadline.Token));
        Assert.True(await events.MoveNextAsync());
        Assert.Equal($"sessions/{host.SessionId}/journal", events.Current.Resource);
        var note = await observer.GetAsync<JournalResponse>($"sessions/{host.SessionId}/journal", deadline.Token);
        Assert.Equal("closed window mid-write", note.Note);
        Assert.Equal(4, note.Rating);
    }

    private sealed class Host(string directory, WebApplication application, WinnowApiClient client,
        GatedSessions sessions, long ownershipId, long sessionId) : IAsyncDisposable
    {
        public WebApplication Application { get; } = application;
        public string Directory { get; } = directory;
        public WinnowApiClient Client { get; } = client;
        public GatedSessions Sessions { get; } = sessions;
        public long OwnershipId { get; } = ownershipId;
        public long SessionId { get; } = sessionId;
        public Task<JournalResponse> Read() => Client.GetAsync<JournalResponse>($"sessions/{SessionId}/journal");
        public Task<JournalResponse> Save(string note, int rating, string revision, CancellationToken ct = default) =>
            Client.SendAsync<SaveJournalRequest, JournalResponse>(HttpMethod.Put,
                $"sessions/{SessionId}/journal", new(note, rating, revision), ct: ct);

        public static async Task<Host> Start(bool finished = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-journal-parity", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<ISessionRepository>(provider => new GatedSessions(
                    new SessionRepository(provider.GetRequiredService<ISqliteConnectionFactory>()),
                    provider.GetRequiredService<IHttpContextAccessor>()));
            });
            try
            {
                var work = await app.Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = "Bluebird" });
                var release = await app.Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = "Bluebird" });
                var ownership = await app.Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership { ReleaseId = release, Store = "manual" });
                var sessions = (GatedSessions)app.Services.GetRequiredService<ISessionRepository>();
                var started = new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);
                var session = await sessions.InsertAsync(new Session { OwnershipId = ownership, StartedAt = started,
                    EndedAt = finished ? started.AddMinutes(47) : null, DurationSeconds = finished ? 47 * 60 : null,
                    DetectionMethod = "manual" });
                await app.StartAsync();
                return new(directory, app, WinnowApiClient.Attach(directory), sessions, ownership, session);
            }
            catch
            {
                await app.DisposeAsync();
                System.IO.Directory.Delete(directory, true);
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            Sessions.Release.TrySetResult();
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class GatedSessions(ISessionRepository inner, IHttpContextAccessor context) : ISessionRepository
    {
        public bool FailNext { get; set; }
        public bool GateWrites { get; set; }
        public int WriteAttempts { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
        {
            WriteAttempts++;
            if (FailNext) { FailNext = false; throw new IOException("Test-owned journal write refusal"); }
            try
            {
                using var disconnected = context.HttpContext?.RequestAborted.Register(() => Disconnected.TrySetResult());
                if (GateWrites) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
                await inner.SetNoteAsync(note, ct);
                Written.TrySetResult(true);
            }
            catch { Written.TrySetResult(false); throw; }
        }
        public Task<long> InsertAsync(Session session, CancellationToken ct = default) => inner.InsertAsync(session, ct);
        public Task<Session?> GetAsync(long id, CancellationToken ct = default) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long id, CancellationToken ct = default) => inner.GetByOwnershipAsync(id, ct);
        public Task<SessionNote?> GetNoteAsync(long id, CancellationToken ct = default) => inner.GetNoteAsync(id, ct);
        public Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(long id, CancellationToken ct = default) => inner.GetJournalEntriesByOwnershipAsync(id, ct);
        public Task DeleteNoteAsync(long id, CancellationToken ct = default) => inner.DeleteNoteAsync(id, ct);
        public Task<Session?> FindOpenMonitoredAsync(long id, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default) => inner.FindOpenMonitoredAsync(id, processes, ct);
        public Task<Session> SaveMonitoredAsync(Session session, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default) => inner.SaveMonitoredAsync(session, processes, ct);
    }
}
