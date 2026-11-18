namespace ImportCEIDGByDate;

public static class CeidgActivityStatus
{
    public static bool IsDeleted(string? status) =>
        string.Equals(status?.Trim(), "Wykreślony", StringComparison.OrdinalIgnoreCase);
}
