using System.IO.Compression;
using System.Text;

namespace PrgAddressImporter;

internal static class SelfTests
{
    public static void Run()
    {
        Equal("JAGIELLONSKA", Normalization.Street("  ul. Jagiellońska "));
        Equal("JANA PAWLA II", Normalization.Street("Aleja Jana Pawła II"));
        Equal("ULANOW", Normalization.Street("Ulanów"));
        Equal("16B", Normalization.Number("16 b"));
        Equal("", Normalization.Province("Brak województwa"));
        Equal("", Normalization.County("Brak powiatu"));
        Equal("MAZOWIECKIE", Normalization.Province("mazowieckie"));
        Equal("TESTOWY", Normalization.County("Testowy"));
        if (Normalization.Key("AB", "C", "1").SequenceEqual(Normalization.Key("A", "BC", "1")))
            throw new Exception("Kolizja podziału klucza.");
        Equal(50, Normalization.SourceId(new string('x', 60), "id").Length);
        var converter = new Coordinates();
        var central = converter.Convert("urn:ogc:def:crs:EPSG::2180", 500000, 500000);
        if (Math.Abs(central.Longitude - 19m) > 0.000001m) throw new Exception("Błędna transformacja EPSG:2180.");
        Equal((4326, 52m, 21m), converter.Convert("EPSG:4326", 52, 21));
        Equal((4326, 52m, 21m), converter.Convert("urn:ogc:def:crs:OGC:1.3:CRS84", 21, 52));
        try
        {
            converter.Convert("EPSG:9999", 1, 2);
            throw new Exception("Nie odrzucono nieznanego SRS.");
        }
        catch (InvalidDataException) { }
        using var fixture = new Fixture();
        using var log = new ImportLog();
        var reader = new SourceReader(log);
        var terc = reader.ReadTerc(fixture.Terc).ToArray();
        Equal("02", terc[0].Code);
        Equal("0201011", terc[2].Code);
        var administrative = AdministrativeNames.Build(terc.Concat(new[]
        {
            new TercRow("0201024", "02", "01", "02", "4", "Suchedniów", "miasto", DateTime.Today),
            new TercRow("0201025", "02", "01", "02", "5", "Suchedniów", "obszar wiejski", DateTime.Today),
            new TercRow("0264", "02", "64", null, null, "Wrocław", "miasto na prawach powiatu", DateTime.Today),
            new TercRow("0264029", "02", "64", "02", "9", "Wrocław-Psie Pole", "delegatura", DateTime.Today)
        }));
        Equal("Suchedniów", administrative["0201023"].Commune);
        Equal("Wrocław", administrative["0264011"].Commune);
        var points = reader.ReadPoints(fixture.Prg, CancellationToken.None).ToArray();
        Equal(1, points.Length);
        Equal("Jagiellońska", points[0].Street);
        Equal("Warszawa", points[0].City);
        Equal("060281614834", points[0].Ulic);
        Equal("fixture:p1", points[0].SourceId);
        PointValidation.ValidateLengths(points[0]);
        PointValidation.ValidateLengths(points[0] with { Ulic = new string('1', 32) });
        try
        {
            PointValidation.ValidateLengths(points[0] with { Ulic = new string('1', 33) });
            throw new Exception("Nie odrzucono kodu przekraczającego limit Ulic.");
        }
        catch (InvalidDataException exception)
        {
            if (!exception.Message.Contains("Ulic: 33") || !exception.Message.Contains("limit 32"))
                throw new Exception("Błąd walidacji nie wskazuje pola i limitu.");
        }
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PrgImporterTest-" + Guid.NewGuid().ToString("N"));
        public string Prg => Path.Combine(directory, "prg.zip");
        public string Terc => Path.Combine(directory, "terc.zip");

        public Fixture()
        {
            Directory.CreateDirectory(directory);
            WriteZip(Terc, "terc.xml", """
                <teryt><catalog><row><WOJ>02</WOJ><POW/><GMI/><RODZ/><NAZWA>TESTOWE</NAZWA><NAZWA_DOD>województwo</NAZWA_DOD><STAN_NA>2026-01-01</STAN_NA></row>
                <row><WOJ>02</WOJ><POW>01</POW><GMI/><RODZ/><NAZWA>Testowy</NAZWA><NAZWA_DOD>powiat</NAZWA_DOD><STAN_NA>2026-01-01</STAN_NA></row>
                <row><WOJ>02</WOJ><POW>01</POW><GMI>01</GMI><RODZ>1</RODZ><NAZWA>Gmina PRG</NAZWA><NAZWA_DOD>gmina</NAZWA_DOD><STAN_NA>2026-01-01</STAN_NA></row></catalog></teryt>
                """);
            // Point precedes both dictionaries and has no direct street reference.
            WriteZip(Prg, "fixture.gml", """
                <gml:FeatureCollection xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:p="https://geoportal.gov.pl/schemas/prgad/1.0" xmlns:xlink="http://www.w3.org/1999/xlink">
                <gml:featureMember><p:AD_PunktAdresowy gml:id="point1"><p:idIIP><p:AD_IdentyfikatorIIP><p:lokalnyId>p1</p:lokalnyId><p:przestrzenNazw>fixture</p:przestrzenNazw></p:AD_IdentyfikatorIIP></p:idIIP><p:numerPorzadkowy>10</p:numerPorzadkowy><p:georeferencja><gml:Point srsName="urn:ogc:def:crs:EPSG::4326"><gml:pos>52 21</gml:pos></gml:Point></p:georeferencja><p:kodPocztowy>00-001</p:kodPocztowy><p:miejscowosc xlink:href="#city1"/></p:AD_PunktAdresowy></gml:featureMember>
                <gml:featureMember><p:AD_UlicaPlac gml:id="street1"><p:nazwaPelna>Jagiellońska</p:nazwaPelna><p:identyfikatorULIC>060281614834</p:identyfikatorULIC><p:miejsce xlink:href="#city1"/><p:adres2 xlink:href="#point1"/></p:AD_UlicaPlac></gml:featureMember>
                <gml:featureMember><p:AD_Miejscowosc gml:id="city1"><p:nazwa>Warszawa</p:nazwa><p:identyfikatorSIMC>0918123</p:identyfikatorSIMC><p:TERYTGminy>0201011</p:TERYTGminy></p:AD_Miejscowosc></gml:featureMember>
                </gml:FeatureCollection>
                """);
        }

        private static void WriteZip(string path, string name, string xml)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Oczekiwano {expected}; otrzymano {actual}.");
    }
}
