using Winnow.Backend;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class BackendEventHubTests
{
    [Fact]
    public async Task TwoClientsReceiveSameCommitAndReconnectReplaysOnlyMissingEvents()
    {
        var hub = new BackendEventHub();
        using var first = hub.Subscribe(null);
        using var second = hub.Subscribe(null);
        Assert.Equal("resync-required", (await first.Reader.ReadAsync()).Kind);
        await second.Reader.ReadAsync();
        hub.Publish("library-changed", "42");
        var a = await first.Reader.ReadAsync();
        Assert.Equal(a, await second.Reader.ReadAsync());
        first.Dispose();
        hub.Publish("library-changed", "43");
        using var reconnect = hub.Subscribe(a.Cursor);
        var replay = await reconnect.Reader.ReadAsync();
        Assert.Equal(a.Sequence + 1, replay.Sequence);
        Assert.Equal("43", replay.Resource);
    }

    [Fact]
    public async Task HistoryGapRestartAndFutureCursorRequireResync()
    {
        var hub = new BackendEventHub(2);
        hub.Publish("one"); hub.Publish("two"); hub.Publish("three");
        foreach (var cursor in new[] { $"{hub.Epoch}:0", "old-epoch:1", $"{hub.Epoch}:99", "malformed" })
        {
            using var subscription = hub.Subscribe(cursor);
            var change = await subscription.Reader.ReadAsync();
            Assert.Equal("resync-required", change.Kind);
            Assert.Equal(3, change.Sequence);
        }
    }

    [Fact]
    public async Task SlowClientResyncDoesNotBlockWriterOrFastClient()
    {
        var hub = new BackendEventHub(2);
        using var slow = hub.Subscribe(null);
        using var fast = hub.Subscribe(null);
        await fast.Reader.ReadAsync();
        for (var i = 0; i < 5; i++)
        {
            hub.Publish("library-changed");
            Assert.Equal("library-changed", (await fast.Reader.ReadAsync()).Kind);
        }
        Assert.Equal("resync-required", (await slow.Reader.ReadAsync()).Kind);
    }
}
