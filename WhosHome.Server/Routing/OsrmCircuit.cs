using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;

namespace WhosHome.Server.Routing;

/// <summary>
/// Stops a dead routing service from costing every incoming report a full timeout. Without this,
/// OSRM being down means each report stalls for the timeout duration before saving, which reads
/// as a broken app rather than a missing nicety.
///
/// It heals by itself: a failure blocks attempts for the cooldown, the next report after that
/// tries again, and a success clears the block. Nothing to restart or reset by hand.
///
/// Deliberately not a real circuit breaker library. One failure opens it, one success closes it,
/// and there is no half-open state to reason about.
/// </summary>
public class OsrmCircuit(IOptions<WhosHomeOptions> options, TimeProvider timeProvider)
{
    private readonly TimeSpan _cooldown = options.Value.OsrmFailureCooldown;

    private long _retryAfterTicks;

    public bool ShouldTry()
    {
        return timeProvider.GetUtcNow().UtcTicks >= Interlocked.Read(ref _retryAfterTicks);
    }

    public void RecordFailure()
    {
        Interlocked.Exchange(
            ref _retryAfterTicks,
            (timeProvider.GetUtcNow() + _cooldown).UtcTicks);
    }

    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _retryAfterTicks, 0);
    }
}
