using System.Globalization;

namespace WhosHome.Server.Ingest;

/// <summary>
/// One decoded OsmAnd protocol report. Traccar Client speaks this over plain HTTP with the
/// values in either the query string or a form body. Only id, lat and lon are mandatory.
/// </summary>
public sealed record OsmAndReport(
    string DeviceId,
    double Latitude,
    double Longitude,
    DateTimeOffset Timestamp,
    double? AccuracyMeters,
    double? BatteryPercent);

public static class OsmAndParser
{
    /// <summary>Below this a numeric timestamp is seconds, above it milliseconds.
    /// Year 5138 in seconds, which is comfortably past the point where this matters.</summary>
    private const long MillisecondThreshold = 100_000_000_000L;

    public static bool TryParse(
        IReadOnlyDictionary<string, string?> values,
        DateTimeOffset receivedUtc,
        out OsmAndReport? report,
        out string? error)
    {
        report = null;

        // The protocol accepts either spelling for the device identifier.
        string? deviceId = Get(values, "id") ?? Get(values, "deviceid");
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            error = "Missing id.";
            return false;
        }

        if (!TryGetDouble(values, "lat", out double latitude))
        {
            error = "Missing or invalid lat.";
            return false;
        }

        if (!TryGetDouble(values, "lon", out double longitude))
        {
            error = "Missing or invalid lon.";
            return false;
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            error = "Coordinates out of range.";
            return false;
        }

        DateTimeOffset timestamp = ParseTimestamp(Get(values, "timestamp"), receivedUtc);

        double? accuracy = TryGetDouble(values, "accuracy", out double accuracyValue) ? accuracyValue : null;
        double? battery = TryGetDouble(values, "batt", out double batteryValue) ? batteryValue : null;

        report = new OsmAndReport(deviceId, latitude, longitude, timestamp, accuracy, battery);
        error = null;
        return true;
    }

    private static DateTimeOffset ParseTimestamp(string? raw, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric))
        {
            return numeric >= MillisecondThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
        }

        // The protocol also documents ISO 8601 and "yyyy-MM-dd HH:mm:ss".
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed.ToUniversalTime();
        }

        return fallback;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, string key)
    {
        return values.TryGetValue(key, out string? value) ? value : null;
    }

    private static bool TryGetDouble(IReadOnlyDictionary<string, string?> values, string key, out double result)
    {
        result = 0;
        string? raw = Get(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
