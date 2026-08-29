using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Monitor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.App.Services;

/// <summary>A finished sitting, as the prompt needs it.</summary>
/// <param name="SessionId">The row the note would be attached to.</param>
/// <param name="OwnershipId">Which game, so the prompt can name it.</param>
/// <param name="DurationSeconds">How long it ran.</param>
public readonly record struct EndedSession(long SessionId, long OwnershipId, long DurationSeconds);

/// <summary>
/// Journal prompt service (§5.2). Opt-in by default (§9 pitfall 7). Subscribes
/// to the session watcher and raises <see cref="SessionEnded"/> only for
/// completed sittings when the preference is on.
/// </summary>
public sealed class SessionJournalService : IDisposable
{
    /// <summary>The settings key for the journal prompt preference.</summary>
    public const string PromptSettingKey = "journal.prompt_after_play";

    /// <summary>
    /// How long <see cref="Dispose"/> blocks for a write still on the wire. The
    /// host disposes this synchronously (`using var host` in Program.cs), so
    /// this is the only seam left that can stop a note write from being
    /// abandoned mid-flight at process exit — it must not hang shutdown forever
    /// if the database never answers.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly ISessionRepository _sessions;
    private readonly ISettingsRepository? _settings;
    private readonly SessionWatcher? _watcher;
    private readonly ILogger<SessionJournalService> _logger;
    private readonly Lock _pendingSavesGate = new();
    private readonly List<Task> _pendingSaves = [];
    private bool _disposed;

    public SessionJournalService(
        ISessionRepository sessions,
        ISettingsRepository? settings = null,
        SessionWatcher? watcher = null,
        ILogger<SessionJournalService>? logger = null)
    {
        _sessions = sessions;
        _settings = settings;
        _watcher = watcher;
        _logger = logger ?? NullLogger<SessionJournalService>.Instance;

        if (_watcher is not null)
        {
            _watcher.SessionRecorded += OnSessionRecorded;
        }
    }

    /// <summary>Raised for a completed session, only when the preference is on.</summary>
    public event EventHandler<EndedSession>? SessionEnded;

    /// <summary>Whether the prompt is enabled. False until the stored preference says otherwise.</summary>
    public bool PromptEnabled { get; private set; }

    /// <summary>Reads the stored preference. Anything unparseable stays off.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        // By key. The settings table also holds the encrypted Epic session, and
        // nothing in this app reads that row except the code that owns it.
        var stored = await _settings.GetAsync(PromptSettingKey, ct).ConfigureAwait(false);
        PromptEnabled = bool.TryParse(stored, out var on) && on;
    }

    /// <summary>Writes the preference. Takes effect immediately, not on restart.</summary>
    public async Task SetPromptEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        PromptEnabled = enabled;

        if (_settings is not null)
        {
            await _settings.SetAsync(PromptSettingKey, enabled ? "true" : "false", ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attaches the note and rating. Either may be null: a rating with no words
    /// is a normal answer, and so is a sentence with no stars.
    /// </summary>
    public Task SaveAsync(long sessionId, string? note, int? rating, CancellationToken ct = default)
    {
        var write = _sessions.SetNoteAsync(
            new SessionNote
            {
                SessionId = sessionId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                Rating = rating is >= 1 and <= 5 ? rating : null,
            },
            ct);

        Track(write);
        return write;
    }

    /// <summary>
    /// Keeps a write reachable from <see cref="Dispose"/> until it settles, so
    /// a shutdown that lands between Save and its completion can still wait for
    /// the row instead of abandoning it.
    /// </summary>
    private void Track(Task write)
    {
        lock (_pendingSavesGate)
        {
            _pendingSaves.Add(write);
        }

        write.ContinueWith(
            _ =>
            {
                lock (_pendingSavesGate)
                {
                    _pendingSaves.Remove(write);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_watcher is not null)
        {
            _watcher.SessionRecorded -= OnSessionRecorded;
        }

        Task[] pending;
        lock (_pendingSavesGate)
        {
            pending = [.. _pendingSaves];
        }

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            Task.WaitAll(pending, DrainTimeout);
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "A journal note failed to save during shutdown.");
        }
    }

    /// <summary>Watcher callback. Runs on the tick thread; must not throw or touch the UI.</summary>
    private void OnSessionRecorded(object? sender, Session session)
    {
        if (!PromptEnabled || session.EndedAt is null || session.Id <= 0)
        {
            return;
        }

        try
        {
            SessionEnded?.Invoke(
                this,
                new EndedSession(session.Id, session.OwnershipId, session.DurationSeconds ?? 0));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A journal-prompt handler threw for session {Id}.", session.Id);
        }
    }
}
