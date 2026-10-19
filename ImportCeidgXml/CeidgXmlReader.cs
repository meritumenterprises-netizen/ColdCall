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
        var provinceFromFile = ProvinceFromFile(file);
        while (await reader.ReadAsync())
        {
            token.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail") continue;
            // Subtree reading leaves the outer reader on the closing Detail, including adjacent records.
            using var subtree = reader.ReadSubtree();
            var element = await XElement.LoadAsync(subtree, LoadOptions.None, token);
            yield return Parse(element, provinceFromFile);
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
        var name = (Value("NazwaPodmiotu", "Firma", "Nazwa")
            ?? string.Join(" ", new[] { Value("Imie"), Value("Nazwisko") }.Where(v => v != null))).Trim();
        DateTime? Date(string field, bool sqlDateTime = false)
        {
            var text = Value(field);
            if (text == null) return null;
            if (!DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "dd.MM.yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw new FormatException($"Nieprawidlowa data {field}: {text}");
            if (sqlDateTime && parsed < new DateTime(1753, 1, 1))
                throw new FormatException($"Data {field} poza zakresem SQL datetime: {text}");
            return parsed;
        }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000)
            throw new FormatException("Brak nazwy/imienia i nazwiska lub nazwa dluzsza niz 1000 znakow.");
        var nip = Value("Nip", "NIP");
        var regon = Value("Regon", "REGON");
        var krs = Value("Krs", "KRS");
        if (nip?.Length > 450 || regon?.Length > 50 || krs?.Length > 450)
            throw new FormatException("NIP, REGON lub KRS przekracza dlugosc kolumny.");
        var province = Value("Wojewodztwo") ?? provinceFromFile;
        var county = Value("Powiat");
        var commune = Value("Gmina");
        var city = Value("Miejscowosc");
        if (new[] { province, county, commune, city }.Any(v => v?.Length > 250))
            throw new FormatException("Nazwa slownika adresowego dluzsza niz 250 znakow.");
        var status = Value("StatusDzialalnosci");
        return new CeidgCompany
        {
            Name = name, Nip = nip, Regon = regon, Krs = krs,
            FirstName = FormatPersonName(Value("Imie")), LastName = FormatPersonName(Value("Nazwisko")),
            Province = province, County = county, Commune = commune, City = city,
            PostalCode = Value("KodPocztowy"), Street = Value("Ulica"),
            BuildingNumber = Value("NrBudynku"), ApartmentNumber = Value("NrLokalu"),
            Phone = Value("Telefon"), Email = Value("Email"), GoogleEmail = Value("GoogleEmail"),
            Website = Value("AdresWWW"), InstagramAccount = Value("InstagramAccount"),
            FacebookAccount = Value("FacebookAccount"),
            StartedAt = Date("DataRozpoczeciaDzialalnosci"),
            SuspendedOn = string.Equals(status, "Zawieszony", StringComparison.OrdinalIgnoreCase)
                ? Date("DataZawieszeniaDzialalnosci", sqlDateTime: true) : null,
            ResumedOn = Date("DataWznowieniaDzialalnosci", sqlDateTime: true)
                ?? Date("DataWznowieniaDzialanosci", sqlDateTime: true),
            Status = status, AddressIsMissing = true, BatchImported = true,
            PrevailingPKD = Value("GlownyKodPkd") ?? "",
            PKDs = (Value("PozostaleKodyPkd") ?? "")
                .Split("$##$", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
        };
    }

    private static string? FormatPersonName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var culture = CultureInfo.GetCultureInfo("pl-PL");
        var lower = value.ToLower(culture);
        return char.ToUpper(lower[0], culture) + lower[1..];
    }

    public static string? ProvinceFromFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        const string marker = "województwo ";
        var index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : name[(index + marker.Length)..].Trim();
    }
}
