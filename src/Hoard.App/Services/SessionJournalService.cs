using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Monitor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.App.Services;

/// <summary>A finished sitting, as the prompt needs it.</summary>
/// <param name="SessionId">The row the note would be attached to.</param>
/// <param name="OwnershipId">Which game, so the prompt can name it.</param>
/// <param name="DurationSeconds">How long it ran.</param>
public readonly record struct EndedSession(long SessionId, long OwnershipId, long DurationSeconds);

/// <summary>
/// §5.2's journal prompt, and §9 pitfall 7's constraint on it.
///
/// <para>The pitfall is quoted in full because it is the whole specification for
/// this class: <i>"Shipping the journal prompt on by default. An unexpected popup
/// after every game exit is an uninstall trigger. Opt-in, explicitly."</i></para>
///
/// <para><b>Off is the default and it is a real default, not a stored one.</b>
/// The setting is absent from a fresh database and absent parses as off, so a
/// user who never opens the preference never sees a prompt in their life. There
/// is no first-run offer, no "would you like to enable journaling?" — an
/// onboarding question about a feature is the same interruption as the feature,
/// moved earlier.</para>
///
/// <para><b>Only completed sittings.</b> A session written with no end time is
/// the shutdown flush recording a game that was still running when Hoard closed
/// (§5.2's in-flight write). Prompting for a note about a game the user has not
/// stopped playing would be nonsense, and prompting as the app exits would be
/// worse.</para>
///
/// <para>§5.1: an App-layer service. It owns the subscription to the watcher and
/// the one settings key, so the view model neither sees <c>Hoard.Monitor</c> nor
/// reads the settings table.</para>
/// </summary>
public sealed class SessionJournalService : IDisposable
{
    /// <summary>
    /// The settings key. Named for the thing the user turns on rather than for
    /// the mechanism, the way <c>DormancyRamp.DimCoversSettingKey</c> is.
    /// </summary>
    public const string PromptSettingKey = "journal.prompt_after_play";

    private readonly ISessionRepository _sessions;
    private readonly ISettingsRepository? _settings;
    private readonly SessionWatcher? _watcher;
    private readonly ILogger<SessionJournalService> _logger;
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

    /// <summary>
    /// Raised for a completed session, and <b>only when the preference is
    /// on</b>. The gate is here rather than in the view model on purpose: a
    /// disabled prompt should not be a window that exists and stays hidden, it
    /// should be an event that is never raised.
    /// </summary>
    public event EventHandler<EndedSession>? SessionEnded;

    /// <summary>
    /// Whether to ask. False until the stored preference says otherwise —
    /// including on a database that has never heard of the key, which is every
    /// database until someone turns it on.
    /// </summary>
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
        => _sessions.SetNoteAsync(
            new SessionNote
            {
                SessionId = sessionId,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                Rating = rating is >= 1 and <= 5 ? rating : null,
            },
            ct);

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
    }

    /// <summary>
    /// Raised on the watcher's tick thread. Nothing here touches the UI — the
    /// view model marshals — and nothing here may throw: the watcher defends its
    /// drain loop against a throwing subscriber, but relying on someone else's
    /// try/catch is not a design.
    /// </summary>
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
