using System.Globalization;

namespace ImportCeidgXml;

public static class ImportSourceDate
{
    public static DateTime Parse(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) || date < new DateTime(1753, 1, 1))
            throw new InvalidDataException(
                $"Nazwa katalogu XML musi byc poprawna data yyyy-MM-dd (od 1753-01-01): {name}");
        return date;
    }
}
