using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PrgAddressImporter;

internal sealed partial class Coordinates
{
    private readonly Dictionary<int, ICoordinateTransformation> transforms = new();

    public (int Srid, decimal Latitude, decimal Longitude) Convert(string srs, double x, double y)
    {
        var match = Epsg().Match(srs);
        var srid = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) :
            srs.EndsWith("CRS84", StringComparison.OrdinalIgnoreCase) ? 4326 : 0;
        double longitude;
        double latitude;
        if (srid == 4326)
        {
            var crs84 = srs.EndsWith("CRS84", StringComparison.OrdinalIgnoreCase);
            latitude = crs84 ? y : x;
            longitude = crs84 ? x : y;
        }
        else if (srid == 2180 || srid is >= 2176 and <= 2179)
        {
            if (!transforms.TryGetValue(srid, out var transform))
            {
                var zone = srid - 2171;
                var central = srid == 2180 ? 19 : zone * 3;
                var scale = srid == 2180 ? "0.9993" : "0.999923";
                var east = srid == 2180 ? 500000 : zone * 1000000 + 500000;
                var north = srid == 2180 ? -5300000 : 0;
                var wkt = $"PROJCS[\"Poland\",GEOGCS[\"ETRF\",DATUM[\"ETRF\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",{central}],PARAMETER[\"scale_factor\",{scale}],PARAMETER[\"false_easting\",{east}],PARAMETER[\"false_northing\",{north}],UNIT[\"metre\",1]]";
                var source = new CoordinateSystemFactory().CreateFromWkt(wkt);
                transform = new CoordinateTransformationFactory().CreateFromCoordinateSystems(source, GeographicCoordinateSystem.WGS84);
                transforms.Add(srid, transform);
            }
            // EPSG Polish GML axes are northing, easting; ProjNet expects easting, northing.
            var result = transform.MathTransform.Transform(new[] { y, x });
            longitude = result[0];
            latitude = result[1];
        }
        else throw new InvalidDataException($"Nieobsługiwany układ: {srs}");
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < 48 or > 56 || longitude is < 13 or > 25)
            throw new InvalidDataException($"Współrzędne poza Polską: {latitude}, {longitude}; {srs}");
        return (srid, (decimal)latitude, (decimal)longitude);
    }

    [GeneratedRegex(@"EPSG(?:::|:|/0/|/)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Epsg();
}
