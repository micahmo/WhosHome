using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;
using WhosHome.Server.Presence;

namespace WhosHome.Server.Notifications;

/// <summary>
/// Announces someone getting close, arriving, and leaving. If you are notified about a person at
/// all, you hear about all three.
/// </summary>
public class PresenceNotifier(
    WhosHomeContext context,
    PushServiceClient pushClient,
    VapidKeys vapidKeys,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    ILogger<PresenceNotifier> logger)
{
    private readonly WhosHomeOptions _options = options.Value;

    private sealed record Announcement(string Title, string Body);

    public async Task NotifyAsync(
        Person person,
        PresenceState previousState,
        PresenceState currentState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);

        Announcement? announcement = Describe(person.Name, previousState, currentState);
        if (announcement is null)
        {
            return;
        }

        // A real announcement means pushes are about to go out, which is the moment a dead
        // subscription would take another one into the void, so it is also the moment to drop it.
        await PruneUnacknowledgedAsync(cancellationToken);

        // Absence of a preference row means the default: hear about everyone except yourself.
        Dictionary<int, bool> preferences = await context.NotificationPreferences
            .AsNoTracking()
            .Where(preference => preference.SubjectPersonId == person.Id)
            .ToDictionaryAsync(
                preference => preference.SubscriberPersonId,
                preference => preference.Enabled,
                cancellationToken);

        List<DeviceSubscription> recipients = (await context.Subscriptions
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .Where(subscription => preferences.TryGetValue(subscription.PersonId, out bool enabled)
                ? enabled
                : subscription.PersonId != person.Id)
            .ToList();

        if (recipients.Count == 0)
        {
            return;
        }

        string payload = JsonSerializer.Serialize(new
        {
            title = announcement.Title,
            body = announcement.Body,
            tag = $"person-{person.Id}",
        });

        PushMessage message = new(payload) { Topic = $"person-{person.Id}" };

        logger.LogInformation(
            "Announcing \"{Title}\" to {Count} device(s).",
            announcement.Title,
            recipients.Count);

        foreach (DeviceSubscription recipient in recipients)
        {
            await SendAsync(recipient, message, cancellationToken);
        }
    }

    /// <summary>
    /// Drops subscriptions we have kept pushing to for longer than
    /// <see cref="WhosHomeOptions.SubscriptionDeadAfter"/> without a single acknowledgement. A
    /// reinstalled or uninstalled app leaves an endpoint the push service still accepts, so delivery
    /// success never removes it and the 410 handler never fires; the silence of its service worker is
    /// the only evidence it is gone. The gap is measured push-to-ack rather than against the clock,
    /// so a subscription in a household that simply had a quiet month is left alone: nothing was
    /// pushed to it to have gone unacknowledged. Filtered in memory because the packed-timestamp
    /// value converter has no SQL for the difference of two of them, and a household is a handful of
    /// rows.
    /// </summary>
    public async Task PruneUnacknowledgedAsync(CancellationToken cancellationToken)
    {
        List<int> dead = (await context.Subscriptions
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .Where(subscription =>
                subscription.LastPushUtc - subscription.LastAckUtc > _options.SubscriptionDeadAfter)
            .Select(subscription => subscription.Id)
            .ToList();

        if (dead.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Dropping {Count} push subscription(s) unacknowledged for over {Days:F0} days.",
            dead.Count,
            _options.SubscriptionDeadAfter.TotalDays);
        await context.Subscriptions
            .Where(subscription => dead.Contains(subscription.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Returns null only for a first-ever report, where there is no previous state to compare to.
    /// Every other change is announced, mirroring arrival and departure: getting close then
    /// arriving on the way in, leaving then gone on the way out. The check order matters, because
    /// leaving home should read as "left" rather than falling through to the outer ring.
    /// </summary>
    private static Announcement? Describe(string name, PresenceState previous, PresenceState current)
    {
        if (previous == PresenceState.Unknown || current == previous)
        {
            return null;
        }

        if (current == PresenceState.Home)
        {
            return new Announcement($"{name} is home", "Just arrived.");
        }

        if (previous == PresenceState.Home)
        {
            return new Announcement($"{name} left", "Just left home.");
        }

        if (current == PresenceState.Nearby)
        {
            // "Nearby" would read as near whoever got the notification, wherever they are.
            return new Announcement($"{name} is near home", "Getting close to home.");
        }

        return new Announcement($"{name} is away", "Left the area.");
    }

    private async Task SendAsync(
        DeviceSubscription recipient,
        PushMessage message,
        CancellationToken cancellationToken)
    {
        PushSubscription subscription = new() { Endpoint = recipient.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, recipient.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, recipient.Auth);

        try
        {
            await pushClient.RequestPushMessageDeliveryAsync(
                subscription,
                message,
                new VapidAuthentication(vapidKeys.PublicKey, vapidKeys.PrivateKey)
                {
                    Subject = _options.VapidSubject,
                },
                cancellationToken);

            // Accepted by the push service. Record the attempt, so the gap against LastAckUtc is
            // measured only against pushes a live worker actually had a chance to acknowledge.
            await context.Subscriptions
                .Where(s => s.Id == recipient.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(s => s.LastPushUtc, timeProvider.GetUtcNow()),
                    cancellationToken);
        }
        catch (PushServiceClientException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.Gone
                or System.Net.HttpStatusCode.NotFound)
        {
            // The browser has thrown the subscription away, so stop trying to reach it. Push
            // services return 410 for this and it is the normal end of a subscription's life.
            logger.LogInformation("Dropping expired push subscription {Id}.", recipient.Id);
            await context.Subscriptions.Where(s => s.Id == recipient.Id).ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // A failed notification must never fail the report that triggered it.
            logger.LogError(exception, "Could not deliver a push notification to subscription {Id}.", recipient.Id);
        }
    }
}
