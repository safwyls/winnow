using System.Threading.Channels;
using Winnow.Api.Contracts.Protocol;

namespace Winnow.Backend;

/// <summary>Atomic replay/subscription registration prevents a commit falling between them.</summary>
public sealed class BackendEventHub : Winnow.Application.IApplicationChangePublisher
{
    private readonly object _gate = new();
    private readonly Queue<BackendEvent> _history = new();
    private readonly HashSet<Channel<BackendEvent>> _subscribers = [];
    private readonly int _capacity;
    private long _sequence;
    public string Epoch { get; } = Guid.NewGuid().ToString("N");
    public long Sequence { get { lock (_gate) return _sequence; } }

    public BackendEventHub(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _capacity = capacity;
    }

    public void Publish(string kind, string? resource = null)
    {
        lock (_gate)
        {
            var change = new BackendEvent(Epoch, ++_sequence, kind, resource, DateTimeOffset.UtcNow);
            _history.Enqueue(change);
            while (_history.Count > _capacity) _history.Dequeue();
            foreach (var subscriber in _subscribers)
            {
                if (subscriber.Writer.TryWrite(change)) continue;
                // A lagging reader receives one new cursor and must reload its snapshots.
                while (subscriber.Reader.TryRead(out _)) { }
                subscriber.Writer.TryWrite(change with { Kind = "resync-required", Resource = null });
            }
        }
    }

    public Subscription Subscribe(string? cursor)
    {
        lock (_gate)
        {
            var channel = Channel.CreateBounded<BackendEvent>(new BoundedChannelOptions(_capacity)
            { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false });
            var parts = cursor?.Split(':');
            var valid = parts is { Length: 2 } && parts[0] == Epoch && long.TryParse(parts[1], out var sequence)
                && sequence >= 0 && sequence <= _sequence && (_history.Count == 0 || sequence >= _history.Peek().Sequence - 1);
            if (!valid)
                channel.Writer.TryWrite(new BackendEvent(Epoch, _sequence, "resync-required", null, DateTimeOffset.UtcNow));
            else
            {
                var after = long.Parse(parts![1], System.Globalization.CultureInfo.InvariantCulture);
                foreach (var change in _history.Where(e => e.Sequence > after)) channel.Writer.TryWrite(change);
            }
            _subscribers.Add(channel);
            return new Subscription(channel.Reader, () => { lock (_gate) { _subscribers.Remove(channel); channel.Writer.TryComplete(); } });
        }
    }

    public sealed class Subscription(ChannelReader<BackendEvent> reader, Action dispose) : IDisposable
    {
        public ChannelReader<BackendEvent> Reader { get; } = reader;
        public void Dispose() => dispose();
    }
}
