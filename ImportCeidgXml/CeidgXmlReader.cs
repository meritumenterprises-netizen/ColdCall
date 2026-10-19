using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

namespace ImportCeidgXml;

public sealed class CeidgXmlReader
{
    public async IAsyncEnumerable<CeidgCompany> ReadAsync(string file,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var settings = new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        };
        using var reader = XmlReader.Create(file, settings);
        while (await reader.ReadAsync())
        {
            token.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail") continue;
            // Subtree reading leaves the outer reader on the closing Detail, including adjacent records.
            using var subtree = reader.ReadSubtree();
            var element = await XElement.LoadAsync(subtree, LoadOptions.None, token);
            yield return Parse(element, ProvinceFromFile(file));
        }
    }

    public static CeidgCompany Parse(XElement element, string? provinceFromFile)
    {
        string? Value(params string[] names)
        {
            foreach (var name in names)
            {
                var value = element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) && value != "-") return value;
            }
            return null;
        }
        var name = Value("NazwaPodmiotu", "Firma", "Nazwa")
            ?? string.Join(" ", new[] { Value("Imie"), Value("Nazwisko") }.Where(v => v != null));
        var startedText = Value("DataRozpoczeciaDzialalnosci");
        DateTime? started = null;
        if (startedText != null)
        {
            if (!DateTime.TryParseExact(startedText, new[] { "yyyy-MM-dd", "dd.MM.yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new FormatException($"Nieprawidlowa data rozpoczecia: {startedText}");
            started = parsed;
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000)
            throw new FormatException("Brak nazwy/imienia i nazwiska lub nazwa dluzsza niz 1000 znakow.");
        var nip = Value("Nip", "NIP");
        var regon = Value("Regon", "REGON");
        if (nip?.Length > 450 || regon?.Length > 50)
            throw new FormatException("NIP lub REGON przekracza dlugosc kolumny.");
        var province = Value("Wojewodztwo") ?? provinceFromFile;
        var county = Value("Powiat");
        var commune = Value("Gmina");
        var city = Value("Miejscowosc");
        if (new[] { province, county, commune, city }.Any(v => v?.Length > 250))
            throw new FormatException("Nazwa slownika adresowego dluzsza niz 250 znakow.");
        return new(name, nip, regon, Value("Imie"), Value("Nazwisko"),
            province, county, commune, city, Value("KodPocztowy"), Value("Ulica"),
            Value("NrBudynku"), Value("NrLokalu"), Value("Telefon"), Value("Email"),
            Value("GoogleEmail"), Value("AdresWWW"), started, Value("StatusDzialalnosci"));
    }

    public static string? ProvinceFromFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        const string marker = "województwo ";
        var index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : name[(index + marker.Length)..].Trim();
    }
}
