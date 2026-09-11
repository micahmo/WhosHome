using System.Net.Http;
using Lib.Net.Http.WebPush;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Notifications;

namespace WhosHome.Tests;

/// <summary>
/// The prune that removes a subscription the push service still accepts but no living service worker
/// answers, which is what a reinstalled or uninstalled app leaves behind. The signal is the gap
/// between the last push accepted and the last one acknowledged, never the wall clock, so a quiet
/// household never loses a live subscription for the sin of nobody moving.
/// </summary>
public class SubscriptionPruneTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_subscription_pushed_to_for_longer_than_the_window_without_an_ack_is_dropped()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        // Acknowledged nothing for forty days, yet still being pushed to as recently as five days
        // ago: a month of announcements landed on it and its worker never answered one.
        await fixture.AddSubscriptionAsync("dead", lastPush: Now.AddDays(-5), lastAck: Now.AddDays(-40));

        await fixture.Notifier.PruneUnacknowledgedAsync(CancellationToken.None);

        Assert.Empty(await fixture.EndpointsAsync());
    }

    [Fact]
    public async Task A_subscription_that_acknowledges_what_it_is_pushed_stays()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        await fixture.AddSubscriptionAsync("live", lastPush: Now.AddHours(-2), lastAck: Now.AddHours(-1));

        await fixture.Notifier.PruneUnacknowledgedAsync(CancellationToken.None);

        Assert.Equal(["live"], await fixture.EndpointsAsync());
    }

    [Fact]
    public async Task A_subscription_that_has_simply_not_been_pushed_in_a_long_time_stays()
    {
        await using Fixture fixture = await Fixture.StartAsync();
        // Both clocks sit far in the past and level: the household was quiet for months, but nothing
        // went unanswered because nothing was sent. The gap, not the age, is what decides.
        await fixture.AddSubscriptionAsync("quiet", lastPush: Now.AddDays(-200), lastAck: Now.AddDays(-200));

        await fixture.Notifier.PruneUnacknowledgedAsync(CancellationToken.None);

        Assert.Equal(["quiet"], await fixture.EndpointsAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WhosHomeContext _context;
        private readonly int _personId;

        private Fixture(SqliteConnection connection, WhosHomeContext context, PresenceNotifier notifier, int personId)
        {
            _connection = connection;
            _context = context;
            Notifier = notifier;
            _personId = personId;
        }

        public PresenceNotifier Notifier { get; }

        public static async Task<Fixture> StartAsync()
        {
            SqliteConnection connection = new("Filename=:memory:");
            await connection.OpenAsync();

            DbContextOptions<WhosHomeContext> contextOptions = new DbContextOptionsBuilder<WhosHomeContext>()
                .UseSqlite(connection)
                .Options;
            WhosHomeContext context = new(contextOptions);
            await context.Database.EnsureCreatedAsync();

            Person person = new() { Name = "Test", DeviceId = "device", CreatedUtc = Now };
            context.People.Add(person);
            await context.SaveChangesAsync();

            IOptions<WhosHomeOptions> options = Options.Create(new WhosHomeOptions());

            // A real push client, never called: these tests only exercise the prune, which sends
            // nothing. The clock is fake but unused, since the prune compares two stored times to each
            // other rather than to now.
            PresenceNotifier notifier = new(
                context,
                new PushServiceClient(new HttpClient()),
                new VapidKeys("public", "private"),
                options,
                new FakeTimeProvider(Now),
                NullLogger<PresenceNotifier>.Instance);

            return new Fixture(connection, context, notifier, person.Id);
        }

        public async Task AddSubscriptionAsync(string endpoint, DateTimeOffset lastPush, DateTimeOffset lastAck)
        {
            _context.Subscriptions.Add(new DeviceSubscription
            {
                PersonId = _personId,
                Endpoint = endpoint,
                P256dh = "key",
                Auth = "auth",
                CreatedUtc = Now,
                LastPushUtc = lastPush,
                LastAckUtc = lastAck,
            });
            await _context.SaveChangesAsync();
        }

        public async Task<string[]> EndpointsAsync()
        {
            return await _context.Subscriptions
                .AsNoTracking()
                .OrderBy(subscription => subscription.Endpoint)
                .Select(subscription => subscription.Endpoint)
                .ToArrayAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
