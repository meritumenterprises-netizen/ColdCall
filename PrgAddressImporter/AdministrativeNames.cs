namespace PrgAddressImporter;

internal sealed record AdministrativeName(string Code, string Province, string County, string Commune);

internal static class AdministrativeNames
{
    public static Dictionary<string, AdministrativeName> Build(IEnumerable<TercRow> source)
    {
        var rows = source.ToArray();
        var dictionary = rows.ToDictionary(x => x.Code);
        var result = new Dictionary<string, AdministrativeName>();
        foreach (var row in rows.Where(x => x.Code.Length == 7))
            Add(row.Code, row.Name);

        // Address TERC contains urban/rural parts (4/5), while PRG may reference their parent (3).
        foreach (var group in rows.Where(x => x.Code.Length == 7 && x.Rodz is "4" or "5").GroupBy(x => x.Code[..6]))
        {
            var urban = group.SingleOrDefault(x => x.Rodz == "4");
            var rural = group.SingleOrDefault(x => x.Rodz == "5");
            if (urban is not null && rural is not null && Normalization.Text(urban.Name) == Normalization.Text(rural.Name))
                Add(group.Key + "3", urban.Name);
        }

        // Address TERC omits the parent municipality of cities divided into districts/delegations.
        foreach (var county in rows.Where(x => x.Code.Length == 4))
        {
            var description = Normalization.Text(county.AdditionalName);
            if (description.Contains("MIASTO", StringComparison.Ordinal) && description.Contains("POWIAT", StringComparison.Ordinal)
                && rows.Any(x => x.Code.StartsWith(county.Code, StringComparison.Ordinal) && x.Rodz is "8" or "9"))
                Add(county.Code + "011", county.Name);
        }
        return result;

        void Add(string code, string commune)
        {
            if (!dictionary.TryGetValue(code[..2], out var province) || !dictionary.TryGetValue(code[..4], out var county))
                throw new InvalidDataException($"Brak nadrzędnej jednostki TERC: {code}");
            result.TryAdd(code, new(code, province.Name, county.Name, commune));
        }
    }
}
