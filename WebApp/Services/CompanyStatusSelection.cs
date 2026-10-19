using System.Text.Json;

namespace ColdCall.Services;

public static class CompanyStatusSelection
{
    public static int[] Parse(string? json)
    {
        if (json == null) return new[] { 1 };
        try { return Normalize(JsonSerializer.Deserialize<int[]>(json)); }
        catch (JsonException ex) { throw new ArgumentException("Niepoprawna lista statusow dzialalnosci.", ex); }
    }

    public static int[] Normalize(int[]? ids)
    {
        ids ??= new[] { 1 };
        if (ids.Length > 255 || ids.Any(id => id < 1 || id > 255))
            throw new ArgumentException("Niepoprawne identyfikatory statusow dzialalnosci.");
        return ids.Distinct().OrderBy(id => id).ToArray();
    }

    public static string Serialize(int[]? ids) => JsonSerializer.Serialize(Normalize(ids));
}
