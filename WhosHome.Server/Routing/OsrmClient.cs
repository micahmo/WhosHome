using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;

namespace WhosHome.Server.Routing;

public sealed record RouteEstimate(double Seconds, double Meters);

/// <summary>
/// Asks a self-hosted OSRM instance how long it takes to drive home.
///
/// The important subtlety: OSRM snaps input coordinates to the nearest road in whichever extract
/// it was built from, and happily answers "Ok" for a point thousands of kilometres outside it.
/// A Los Angeles coordinate against a US Northeast extract returns a confident 611 mi / 797 min
/// that is entirely fabricated. So a plausible snap distance is the real success condition, not
/// the status code.
/// </summary>
public class OsrmClient(
    HttpClient httpClient,
    OsrmCircuit circuit,
    IOptions<WhosHomeOptions> options,
    ILogger<OsrmClient> logger)
{
    private readonly WhosHomeOptions _options = options.Value;

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.OsrmBaseUrl);

    /// <summary>
    /// Returns null whenever the answer cannot be trusted: routing disabled, OSRM unreachable, no
    /// route found, or a coordinate outside the extract. Callers fall back to straight-line
    /// distance, which is why this never throws.
    /// </summary>
    public async Task<RouteEstimate?> TryGetDriveHomeAsync(
        double fromLatitude,
        double fromLongitude,
        CancellationToken cancellationToken)
    {
        if (!Enabled || !circuit.ShouldTry())
        {
            return null;
        }

        string baseUrl = _options.OsrmBaseUrl!.TrimEnd('/');
        string from = Coordinate(fromLongitude, fromLatitude);
        string home = Coordinate(_options.HomeLongitude, _options.HomeLatitude);
        string url = $"{baseUrl}/route/v1/driving/{from};{home}?overview=false";

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("OSRM returned {Status}.", (int)response.StatusCode);
                circuit.RecordFailure();
                return null;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            // A reachable OSRM closes the circuit even when the answer is rejected as implausible:
            // an out-of-region coordinate is a normal outcome, not a sick service.
            circuit.RecordSuccess();
            return Interpret(document.RootElement);
        }
        catch (Exception exception)
        {
            // Routing is enrichment. Losing it must never cost us the position report.
            logger.LogDebug(exception, "Could not reach OSRM at {Url}.", baseUrl);
            circuit.RecordFailure();
            return null;
        }
    }

    private RouteEstimate? Interpret(JsonElement root)
    {
        if (!root.TryGetProperty("code", out JsonElement code)
            || code.GetString() != "Ok")
        {
            return null;
        }

        if (!root.TryGetProperty("waypoints", out JsonElement waypoints)
            || waypoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement waypoint in waypoints.EnumerateArray())
        {
            if (!waypoint.TryGetProperty("distance", out JsonElement snap))
            {
                return null;
            }

            double snapMeters = snap.GetDouble();
            if (snapMeters > _options.OsrmMaxSnapMeters)
            {
                // Outside the extract. The route that came back is between two arbitrary snapped
                // points and describes a journey nobody is making.
                logger.LogDebug(
                    "Discarding OSRM route: coordinate snapped {Snap:F0} m, over the {Limit:F0} m limit.",
                    snapMeters,
                    _options.OsrmMaxSnapMeters);
                return null;
            }
        }

        if (!root.TryGetProperty("routes", out JsonElement routes)
            || routes.ValueKind != JsonValueKind.Array
            || routes.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement route = routes[0];
        if (!route.TryGetProperty("duration", out JsonElement duration)
            || !route.TryGetProperty("distance", out JsonElement distance))
        {
            return null;
        }

        return new RouteEstimate(duration.GetDouble(), distance.GetDouble());
    }

    private static string Coordinate(double longitude, double latitude)
    {
        // OSRM wants longitude first, and invariant formatting so a comma decimal separator on the
        // host locale cannot corrupt the request.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{longitude:0.######},{latitude:0.######}");
    }
}
