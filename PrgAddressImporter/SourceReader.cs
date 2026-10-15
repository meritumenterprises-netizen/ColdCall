using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PrgAddressImporter;

internal sealed record TercRow(string Code, string Woj, string? Pow, string? Gmi, string? Rodz,
    string Name, string? AdditionalName, DateTime State);
internal sealed record AddressPoint(string SourceId, string GmlId, string Terc, string Simc,
    string City, string Ulic, string Street, string Number, string PostalCode,
    decimal X, decimal Y, int Srid, decimal Latitude, decimal Longitude);

internal sealed class SourceReader(ImportLog log)
{
    private static readonly XNamespace Gml = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    private readonly Coordinates coordinates = new();

    public IEnumerable<TercRow> ReadTerc(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.Single(x => Path.GetExtension(x.FullName).Equals(".xml", StringComparison.OrdinalIgnoreCase));
        foreach (var row in Features(entry, "row"))
        {
            var woj = Value(row, "WOJ");
            var pow = Empty(Value(row, "POW"));
            var gmi = Empty(Value(row, "GMI"));
            var rodz = Empty(Value(row, "RODZ"));
            yield return new(woj + pow + gmi + rodz, woj, pow, gmi, rodz,
                Value(row, "NAZWA"), Empty(Value(row, "NAZWA_DOD")),
                DateTime.ParseExact(Value(row, "STAN_NA"), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }

    public IEnumerable<AddressPoint> ReadPoints(string path, CancellationToken token, bool firstEntryOnly = false)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(x => Path.GetExtension(x.FullName).Equals(".gml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length == 0) throw new InvalidDataException("ZIP nie zawiera plików GML.");
        foreach (var entry in firstEntryOnly ? entries.Take(1) : entries)
        {
            log.Write("READ", $"{entry.FullName}: słowniki miejscowości, ulic i odwołań adresowych.");
            var cities = new Dictionary<string, City>();
            var streets = new Dictionary<string, Street>();
            var pointStreets = new Dictionary<string, string>();
            foreach (var feature in Features(entry, "AD_Miejscowosc", "AD_UlicaPlac"))
            {
                token.ThrowIfCancellationRequested();
                var id = (string?)feature.Attribute(Gml + "id") ?? throw new InvalidDataException("Brak gml:id.");
                if (feature.Name.LocalName == "AD_Miejscowosc")
                    cities.Add(id, new(Value(feature, "nazwa"), Value(feature, "identyfikatorSIMC"), Value(feature, "TERYTGminy")));
                else
                {
                    streets.Add(id, new(Value(feature, "nazwaPelna"), Value(feature, "identyfikatorULIC"), Reference(feature, "miejsce")));
                    foreach (var reference in feature.Elements().Where(x => x.Name.LocalName == "adres2"))
                    {
                        var pointId = ((string?)reference.Attribute(Xlink + "href") ?? "").TrimStart('#');
                        if (pointId.Length == 0) continue;
                        if (pointStreets.TryGetValue(pointId, out var previous) && previous != id)
                            throw new InvalidDataException($"Punkt przypisany do wielu ulic: {pointId}");
                        pointStreets[pointId] = id;
                    }
                }
            }
            log.Write("READ", $"{entry.FullName}: {cities.Count:N0} miejscowości, {streets.Count:N0} ulic, {pointStreets.Count:N0} powiązań; odczyt punktów.");
            long count = 0;
            foreach (var feature in Features(entry, "AD_PunktAdresowy"))
            {
                token.ThrowIfCancellationRequested();
                var id = (string?)feature.Attribute(Gml + "id") ?? throw new InvalidDataException("Brak gml:id.");
                var streetRef = Reference(feature, "ulica");
                if (streetRef.Length == 0) pointStreets.TryGetValue(id, out streetRef);
                Street? street = null;
                if (!string.IsNullOrEmpty(streetRef) && !streets.TryGetValue(streetRef, out street))
                    throw new InvalidDataException($"Nierozwiązana ulica: {streetRef}");
                var cityRef = Reference(feature, "miejscowosc");
                if (cityRef.Length == 0) cityRef = street?.CityReference ?? "";
                if (!cities.TryGetValue(cityRef, out var city)) throw new InvalidDataException($"Nierozwiązana miejscowość: {cityRef}");
                var point = feature.Descendants(Gml + "Point").Single();
                var srs = (string?)point.Attribute("srsName") ?? throw new InvalidDataException("Brak srsName.");
                var pos = point.Element(Gml + "pos")?.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    ?? throw new InvalidDataException("Brak gml:pos.");
                if (pos.Length != 2) throw new InvalidDataException("Oczekiwano geometrii 2D.");
                var x = double.Parse(pos[0], CultureInfo.InvariantCulture);
                var y = double.Parse(pos[1], CultureInfo.InvariantCulture);
                var gps = coordinates.Convert(srs, x, y);
                var localId = Value(feature, "lokalnyId");
                var space = Value(feature, "przestrzenNazw");
                if (localId.Length == 0 || space.Length == 0 || city.Terc.Length != 7)
                    throw new InvalidDataException($"Niekompletny identyfikator punktu: {id}");
                yield return new(Normalization.SourceId(space, localId), id, city.Terc, city.Simc, city.Name,
                    street?.Ulic ?? "", street?.Name ?? "", Value(feature, "numerPorzadkowy"),
                    Value(feature, "kodPocztowy"), (decimal)x, (decimal)y, gps.Srid, gps.Latitude, gps.Longitude);
                count++;
            }
            log.Write("READ", $"{entry.FullName}: odczytano {count:N0} punktów.");
        }
    }

    internal static IEnumerable<XElement> Features(ZipArchiveEntry entry, params string[] names)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && wanted.Contains(reader.LocalName))
            {
                // Only one feature is materialized, never the complete GML document.
                yield return (XElement)XNode.ReadFrom(reader);
            }
            else reader.Read();
        }
    }

    private static string Value(XElement element, string name) => element.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? "";
    private static string Reference(XElement element, string name) =>
        ((string?)element.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Attribute(Xlink + "href") ?? "").TrimStart('#');
    private static string? Empty(string value) => value.Length == 0 ? null : value;
    private sealed record City(string Name, string Simc, string Terc);
    private sealed record Street(string Name, string Ulic, string CityReference);
}
