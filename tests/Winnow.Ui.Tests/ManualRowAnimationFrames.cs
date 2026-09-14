using Winnow.App.Views.Fullscreen;

namespace Winnow.Ui.Tests;

internal sealed class ManualRowAnimationFrames
{
    private readonly Queue<Action<TimeSpan>> _pending = new();

    public ManualRowAnimationFrames(FullscreenRowViewport viewport) => viewport.FrameScheduler = _pending.Enqueue;

    public void Tick(TimeSpan timestamp)
    {
        var callbacks = _pending.ToArray();
        _pending.Clear();
        foreach (var callback in callbacks) callback(timestamp);
    }
}
