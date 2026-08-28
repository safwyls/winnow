using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Monitor;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// §5.2's journal prompt against §9 pitfall 7, which is the specification for
/// it: <i>"Shipping the journal prompt on by default. An unexpected popup after
/// every game exit is an uninstall trigger. Opt-in, explicitly."</i>
///
/// <para>The first three tests are the pitfall itself, and they are the ones
/// worth breaking the build over: off is the default, off means the event is
/// never raised, and a session still running is never asked about.</para>
/// </summary>
public sealed class JournalPromptTests
{
    private static readonly DateTime T0 = new(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <b>Pitfall 7, stated as a test.</b> A fresh database has no row for the
    /// key, and an absent row is off — so a user who never opens the preference
    /// never sees this feature exist.
    /// </summary>
    [Fact]
    public async Task The_prompt_is_off_on_a_database_that_has_never_heard_of_it()
    {
        using var db = new TempDatabase();
        var journal = new SessionJournalService(
            new SessionRepository(db.Factory), new SettingsRepository(db.Factory));

        await journal.LoadAsync();

        Assert.False(journal.PromptEnabled);
    }

    /// <summary>
    /// Off is not a hidden card — it is an event that never happens. A recorded
    /// session with the preference off reaches nothing at all.
    /// </summary>
    [Fact]
    public async Task With_the_prompt_off_a_finished_session_raises_nothing()
    {
        using var fixture = new JournalFixture();
        await fixture.Journal.LoadAsync();

        var raised = 0;
        fixture.Journal.SessionEnded += (_, _) => raised++;

        await fixture.RecordSessionAsync(endsAt: T0.AddMinutes(47));

        Assert.Equal(0, raised);
        Assert.False(fixture.Prompt.IsOpen);
    }

    /// <summary>
    /// Turned on, it asks — and it names the game and the sitting, because a
    /// note is only worth writing against something you recognise.
    /// </summary>
    [Fact]
    public async Task With_the_prompt_on_a_finished_session_opens_the_card()
    {
        using var fixture = new JournalFixture();
        await fixture.Journal.SetPromptEnabledAsync(true);

        await fixture.RecordSessionAsync(endsAt: T0.AddMinutes(47));

        Assert.True(fixture.Prompt.IsOpen);
        Assert.Equal("Bluebird", fixture.Prompt.Title);
        Assert.Equal("47m", fixture.Prompt.DurationText);
        Assert.False(fixture.Prompt.HasContent);
    }

    /// <summary>
    /// A session written with no end time is the shutdown flush recording a game
    /// that is STILL RUNNING. Asking about it would be nonsense, and asking as
    /// the app exits would be worse.
    /// </summary>
    [Fact]
    public async Task An_in_flight_session_is_never_asked_about()
    {
        using var fixture = new JournalFixture();
        await fixture.Journal.SetPromptEnabledAsync(true);

        await fixture.RecordSessionAsync(endsAt: null);

        Assert.False(fixture.Prompt.IsOpen);
    }

    /// <summary>
    /// Dismissal is not an answer and costs nothing. The session row is already
    /// written and complete; a note is an optional annotation on it.
    /// </summary>
    [Fact]
    public async Task Dismissing_writes_nothing()
    {
        using var fixture = new JournalFixture();
        await fixture.Journal.SetPromptEnabledAsync(true);
        var sessionId = await fixture.RecordSessionAsync(endsAt: T0.AddMinutes(47));

        fixture.Prompt.DismissCommand.Execute(null);

        Assert.False(fixture.Prompt.IsOpen);
        Assert.Null(await fixture.Sessions.GetNoteAsync(sessionId));
    }

    /// <summary>The words and the rating both land, and the card closes.</summary>
    [Fact]
    public async Task Saving_attaches_the_note_and_the_rating()
    {
        using var fixture = new JournalFixture();
        await fixture.Journal.SetPromptEnabledAsync(true);
        var sessionId = await fixture.RecordSessionAsync(endsAt: T0.AddMinutes(47));

        fixture.Prompt.RateCommand.Execute("4");
        fixture.Prompt.Note = "  bounced off the second boss  ";
        fixture.Prompt.SaveCommand.Execute(null);

        await fixture.Prompt.PendingSave;

        var note = await fixture.Sessions.GetNoteAsync(sessionId);
        Assert.NotNull(note);
        Assert.Equal("bounced off the second boss", note!.Note);
        Assert.Equal(4, note.Rating);
        Assert.False(fixture.Prompt.IsOpen);
    }

    /// <summary>
    /// A rating given by accident has to be retractable, or the card has become
    /// a thing you cannot leave without answering.
    /// </summary>
    [Fact]
    public void Pressing_the_set_rating_again_clears_it()
    {
        using var prompt = new JournalPromptViewModel(post: a => a());
        prompt.Open(new EndedSession(1, 1, 600), "Bluebird");

        prompt.RateCommand.Execute("3");
        Assert.Equal(3, prompt.Rating);

        prompt.RateCommand.Execute("3");
        Assert.Equal(0, prompt.Rating);
        Assert.False(prompt.HasContent);
    }

    /// <summary>
    /// A second game ending mid-sentence does not take the sentence away. The
    /// newer session keeps its row and loses its prompt, which is the cheaper of
    /// the two losses.
    /// </summary>
    [Fact]
    public void A_second_session_never_interrupts_a_note_being_typed()
    {
        using var prompt = new JournalPromptViewModel(post: a => a());
        prompt.Open(new EndedSession(1, 1, 600), "Bluebird");
        prompt.Note = "half a thought";

        prompt.Open(new EndedSession(2, 2, 900), "Redwing");

        Assert.Equal("Bluebird", prompt.Title);
        Assert.Equal("half a thought", prompt.Note);
    }

    /// <summary>An untouched card is replaced freely — nothing is being lost.</summary>
    [Fact]
    public void A_second_session_replaces_an_untouched_card()
    {
        using var prompt = new JournalPromptViewModel(post: a => a());
        prompt.Open(new EndedSession(1, 1, 600), "Bluebird");
        prompt.Open(new EndedSession(2, 2, 5400), "Redwing");

        Assert.Equal("Redwing", prompt.Title);
        Assert.Equal("1h 30m", prompt.DurationText);
    }

    /// <summary>
    /// Ignored, it removes itself. "Dismissible without a decision" includes not
    /// having to make the dismissing gesture either.
    /// </summary>
    [Fact]
    public void An_untouched_card_dismisses_itself()
    {
        var clock = new FakeTimeProvider(T0);
        using var prompt = new JournalPromptViewModel(clock: clock, post: a => a());
        prompt.Open(new EndedSession(1, 1, 600), "Bluebird");

        clock.Advance(JournalPromptViewModel.Patience + TimeSpan.FromSeconds(1));

        Assert.False(prompt.IsOpen);
    }

    /// <summary>
    /// A card being typed into does NOT vanish under the cursor — every
    /// interaction restarts the countdown, so the timer can only ever fire on a
    /// card nobody has touched.
    /// </summary>
    [Fact]
    public void A_card_with_a_note_in_it_stays_put()
    {
        var clock = new FakeTimeProvider(T0);
        using var prompt = new JournalPromptViewModel(clock: clock, post: a => a());
        prompt.Open(new EndedSession(1, 1, 600), "Bluebird");
        prompt.Note = "still writing";

        clock.Advance(JournalPromptViewModel.Patience + TimeSpan.FromSeconds(1));

        Assert.True(prompt.IsOpen);
        Assert.Equal("still writing", prompt.Note);
    }

    /// <summary>
    /// A game the loaded library cannot name gets no card. Naming it "this game"
    /// would be a puzzle rather than a question.
    /// </summary>
    [Fact]
    public async Task A_session_for_a_game_the_library_cannot_name_is_not_asked_about()
    {
        using var fixture = new JournalFixture(titleFor: _ => null);
        await fixture.Journal.SetPromptEnabledAsync(true);

        await fixture.RecordSessionAsync(endsAt: T0.AddMinutes(47));

        Assert.False(fixture.Prompt.IsOpen);
    }

    /// <summary>
    /// The whole path in one: a real watcher on a real database records a real
    /// session, and the card opens because of it. No game, no timer, no sleep.
    /// </summary>
    private sealed class JournalFixture : IDisposable
    {
        private readonly SessionWatcherHarness _harness = new();

        public JournalFixture(Func<long, string?>? titleFor = null)
        {
            Sessions = _harness.Sessions;
            Journal = new SessionJournalService(
                Sessions, _harness.Settings, _harness.Watcher);
            Prompt = new JournalPromptViewModel(
                Journal, titleFor ?? (_ => "Bluebird"), post: a => a());
        }

        public SessionRepository Sessions { get; }

        public SessionJournalService Journal { get; }

        public JournalPromptViewModel Prompt { get; }

        /// <summary>
        /// Runs a whole sitting through the watcher and returns the session id
        /// it wrote. <paramref name="endsAt"/> null leaves the game running and
        /// takes the shutdown-flush path instead.
        /// </summary>
        public async Task<long> RecordSessionAsync(DateTime? endsAt)
        {
            var game = await _harness.AddGameAsync("Bluebird", "bluebird.exe");
            _harness.Processes.Start(900, "bluebird", game.Exe("bluebird.exe"), T0);
            await _harness.TickAtAsync(T0.AddSeconds(5));

            if (endsAt is { } ended)
            {
                _harness.Processes.Exit(900, ended);
                await _harness.TickAtAsync(ended.AddMinutes(1));
            }
            else
            {
                _harness.Clock.SetUtcNow(T0.AddMinutes(47));
                await _harness.Watcher.FlushAsync();
            }

            var sessions = await _harness.SessionsForAsync(game.OwnershipId);
            return sessions.Single().Id;
        }

        public void Dispose()
        {
            Prompt.Dispose();
            Journal.Dispose();
            _harness.Dispose();
        }
    }
}
