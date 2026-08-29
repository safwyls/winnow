using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Monitor;

namespace Winnow.App.ViewModels;

/// <summary>
/// Non-modal status strip shown after Play is pressed. Resolves when the session
/// watcher confirms a running process via <see cref="LaunchIntents"/>, or expires
/// quietly if no process appears. The only negative message is a refused URI.
/// </summary>
public partial class LaunchStatusViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long "X is running" stays up after the watcher confirms it. Long
    /// enough to be read by someone still looking at Winnow, short enough to be
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

    /// <summary>Winnow has handed the launch off and is waiting to see the game.</summary>
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
