using System.Text.RegularExpressions;
using AdjustCeidgAddress.Data.FirmyTemp;

namespace AdjustCeidgAddress;

public static class ProvinceFileName
{
    public static int? ResolveId(string file, IEnumerable<Wojewodztwa> provinces)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(file),
            @"województwo\s+(?<name>[^_]+)(?:_.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        var name = Normalize(match.Groups["name"].Value);
        return provinces.SingleOrDefault(province =>
            string.Equals(Normalize(province.Name), name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string Normalize(string name) => Regex.Replace(name.Replace('-', ' '), @"\s+", " ").Trim();
}
