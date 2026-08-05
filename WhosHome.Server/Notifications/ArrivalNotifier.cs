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
/// Sends a notification when someone gets closer to home, crossing into the nearby ring or
/// arriving. Departures are deliberately silent: nobody needs a push saying a housemate left.
/// </summary>
public class ArrivalNotifier(
    WhosHomeContext context,
    PushServiceClient pushClient,
    VapidKeys vapidKeys,
    IOptions<WhosHomeOptions> options,
    ILogger<ArrivalNotifier> logger)
{
    private readonly WhosHomeOptions _options = options.Value;

    public async Task NotifyIfApproachingAsync(
        Person person,
        PresenceState previousState,
        PresenceState currentState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(person);

        if (!IsApproaching(previousState, currentState))
        {
            return;
        }

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

        string title = currentState == PresenceState.Home
            ? $"{person.Name} is home"
            : $"{person.Name} is nearby";

        string payload = JsonSerializer.Serialize(new
        {
            title,
            body = currentState == PresenceState.Home ? "Just arrived." : "Getting close to home.",
            tag = $"person-{person.Id}",
        });

        PushMessage message = new(payload) { Topic = $"person-{person.Id}" };

        foreach (DeviceSubscription recipient in recipients)
        {
            await SendAsync(recipient, message, cancellationToken);
        }
    }

    /// <summary>
    /// Relies on <see cref="PresenceState"/> being ordered closest to furthest, so getting nearer
    /// is a numeric decrease. Unknown is zero, which conveniently means a first-ever report never
    /// counts as an arrival.
    /// </summary>
    private static bool IsApproaching(PresenceState previous, PresenceState current)
    {
        if (previous == PresenceState.Unknown)
        {
            return false;
        }

        return current < previous
            && current is PresenceState.Home or PresenceState.Nearby;
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
