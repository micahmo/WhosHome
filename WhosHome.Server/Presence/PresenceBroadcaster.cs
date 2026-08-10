using System.Collections.Concurrent;
using System.Threading.Channels;

namespace WhosHome.Server.Presence;

/// <summary>
/// Wakes open board connections when something has changed.
///
/// Deliberately carries no payload. A listener that has been woken goes and reads the current state
/// for itself, so a connection that was busy writing cannot fall behind and start serving a queue of
/// increasingly old snapshots. The only thing being communicated is "look again".
/// </summary>
public sealed class PresenceBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<byte>> _listeners = new();

    public (Guid Id, ChannelReader<byte> Reader) Subscribe()
    {
        // Bounded at one and dropping writes, which coalesces. A phone flushing forty buffered
        // reports in one burst wakes each connection once, not forty times, and the single read
        // that follows already reflects all forty.
        Channel<byte> channel = Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        Guid id = Guid.NewGuid();
        _listeners[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        _listeners.TryRemove(id, out _);
    }

    /// <summary>
    /// Never throws and never blocks. This runs inline with an incoming report, and a broken
    /// listener must not be able to fail the report that woke it.
    /// </summary>
    public void Signal()
    {
        foreach (Channel<byte> listener in _listeners.Values)
        {
            listener.Writer.TryWrite(0);
        }
    }
}
