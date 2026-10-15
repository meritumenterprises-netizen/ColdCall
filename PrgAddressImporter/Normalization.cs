using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrgAddressImporter;

internal static partial class Normalization
{
    public static string Text(string? value)
    {
        var decomposed = (value ?? "").Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                text.Append(character);
        return Spaces().Replace(text.ToString().ToUpperInvariant(), " ").Trim();
    }

    public static string Street(string? value) => Prefix().Replace(Text(value), "");
    public static string Number(string? value) => Spaces().Replace(Text(value), "");

    public static string Province(string? value) => Text(value) switch
    {
        "BRAK WOJEWODZTWA" or "-" => "",
        var name => name
    };

    public static string County(string? value) => Text(value) switch
    {
        "BRAK POWIATU" or "-" => "",
        var name => name
    };

    public static byte[] Key(string city, string street, string number) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{city.Length}:{city}{street.Length}:{street}{number.Length}:{number}"));

    public static string SourceId(string space, string localId)
    {
        var id = $"{space}:{localId}";
        return id.Length <= 50 ? id : "PRG:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..46];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"^(?:ULICA|UL|ALEJA|ALEJE|AL|PLAC|PL|OSIEDLE|OS)(?:\.\s*|\s+)")]
    private static partial Regex Prefix();
}
