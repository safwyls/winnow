using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.Monitor;

namespace Hoard.App.ViewModels;

/// <summary>
/// The ambient answer to "did that work?" after Play is pressed.
///
/// <para><b>The problem it solves, stated exactly.</b> A cold Steam client
/// starting a large game can take thirty seconds or more before anything appears
/// on screen. Doing nothing for thirty seconds after a click reads as a broken
/// button, and a user who concludes the button is broken clicks it again — two
/// store prompts, or worse. So something has to acknowledge the press.</para>
///
/// <para><b>Why it is a line of text and not a dialog, a spinner overlay or a
/// progress bar.</b> A modal takes the window hostage while the user waits for a
/// different application to do something; that is friction charged for nothing,
/// and the user's brief for this milestone names it. A blocking spinner is the
/// same idea with worse manners. A progress bar would be a lie — Hoard has no
/// idea how far along Steam is and cannot get one. What is left is a small strip
/// that says what is happening, occupies no attention, and gets out of the way
/// on its own.</para>
///
/// <para><b>It resolves off the same signal that proves the launch worked.</b>
/// The waiting state ends when the session watcher attaches to a process for
/// this ownership, not on a timer — which means the indicator disappearing is a
/// real fact about a real running game rather than an animation finishing. That
/// is the one thing a launcher can say that a spinner cannot, and it exists only
/// because §5.2's watcher and M3b's launch share
/// <see cref="LaunchIntents"/>.</para>
///
/// <para><b>Silence is a state, and it is the common one.</b> A launch that never
/// produces a process — the user cancelled at Steam's own prompt, or thought
/// better of it — expires and the strip simply goes away. It does not say
/// "launch failed", because nothing failed; the user changed their mind, and
/// being told off for it is exactly the friction this milestone is spending its
/// budget to avoid. The one message with a negative tone is reserved for the one
/// case Hoard actually knows went wrong: the URI never reached a handler.</para>
/// </summary>
public partial class LaunchStatusViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long "X is running" stays up after the watcher confirms it. Long
    /// enough to be read by someone still looking at Hoard, short enough to be
    /// gone before they alt-tab back.
    /// </summary>
    private static readonly TimeSpan ConfirmedFor = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a refusal stays up. Longer than a confirmation because it is the
    /// only one carrying information the user may want to act on, and still
    /// short enough that it cannot become furniture.
    /// </summary>
    private static readonly TimeSpan RefusalFor = TimeSpan.FromSeconds(7);

    private readonly LaunchIntents? _intents;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;
    private readonly TimeSpan _patience;

    private ITimer? _timer;
    private long? _waitingFor;
    private string _waitingTitle = string.Empty;
    private bool _disposed;

    /// <param name="post">
    /// How to get onto the UI thread. Injected rather than calling
    /// <c>Dispatcher.UIThread</c> directly so the whole state machine is
    /// testable with no window: the watcher raises its event on a tick thread,
    /// and this is the seam that fact crosses.
    /// </param>
    public LaunchStatusViewModel(
        LaunchIntents? intents = null,
        TimeProvider? clock = null,
        Action<Action>? post = null,
        TimeSpan? patience = null)
    {
        _intents = intents;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

        // Matched to the registry's own window by default. A strip that outlived
        // the intent behind it would be waiting for something that can no longer
        // happen.
        _patience = patience ?? intents?.Window ?? TimeSpan.FromSeconds(90);

        if (_intents is not null)
        {
            _intents.Observed += OnObserved;
        }
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>Drives the pulsing dot: true only while the outcome is unknown.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    /// <summary>Draws the strip in Amber rather than Volt. See the type remarks.</summary>
    [ObservableProperty]
    public partial bool IsProblem { get; set; }

    /// <summary>Hoard has handed the launch off and is waiting to see the game.</summary>
    public void Waiting(long ownershipId, string title)
    {
        _waitingFor = ownershipId;
        _waitingTitle = title;
        Show($"Starting {title}…", waiting: true, problem: false);

        // The only timer in the waiting state, and it is a floor under the strip
        // rather than the thing that resolves it: a launch nobody ever sees
        // running must not leave a line of text on screen for the rest of the
        // evening.
        Arm(_patience, () =>
        {
            if (_waitingFor == ownershipId)
            {
                Close();
            }
        });
    }

    /// <summary>The URI never reached a handler. The one case worth naming.</summary>
    public void Refused(string title, string store)
    {
        _waitingFor = null;
        Show($"Couldn't reach {store} to start {title}.", waiting: false, problem: true);
        Arm(RefusalFor, Close);
    }

    /// <summary>
    /// Clears the strip. Called when a click finds a launch already in flight —
    /// nothing changes, the first click's strip is still the answer — and by the
    /// timers above.
    /// </summary>
    public void Close()
    {
        _waitingFor = null;
        IsWaiting = false;
        IsProblem = false;
        IsOpen = false;
        Message = string.Empty;
        Disarm();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_intents is not null)
        {
            _intents.Observed -= OnObserved;
        }

        Disarm();
    }

    /// <summary>
    /// The watcher saw it. Raised on the watcher's tick thread, so everything
    /// this touches is marshalled first.
    /// </summary>
    private void OnObserved(object? sender, LaunchObserved observed)
        => _post(() =>
        {
            if (_waitingFor != observed.OwnershipId)
            {
                return;
            }

            _waitingFor = null;
            Show($"{_waitingTitle} is running.", waiting: false, problem: false);
            Arm(ConfirmedFor, Close);
        });

    private void Show(string message, bool waiting, bool problem)
    {
        Message = message;
        IsWaiting = waiting;
        IsProblem = problem;
        IsOpen = true;
    }

    private void Arm(TimeSpan after, Action then)
    {
        Disarm();
        _timer = _clock.CreateTimer(
            _ => _post(then), null, after, Timeout.InfiniteTimeSpan);
    }

    private void Disarm()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
