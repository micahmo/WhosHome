namespace WhosHome.Server.Presence;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_000d;

    /// <summary>
    /// Great-circle distance in meters. Good to a fraction of a percent at household
    /// distances, which is far tighter than the GPS accuracy feeding it.
    /// </summary>
    public static double DistanceMeters(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        double latitudeARadians = double.DegreesToRadians(latitudeA);
        double latitudeBRadians = double.DegreesToRadians(latitudeB);
        double deltaLatitude = double.DegreesToRadians(latitudeB - latitudeA);
        double deltaLongitude = double.DegreesToRadians(longitudeB - longitudeA);

        double a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2)
            + Math.Cos(latitudeARadians) * Math.Cos(latitudeBRadians)
            * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }
}
