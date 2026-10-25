using System.Text.RegularExpressions;

namespace ImportCeidgXml;

public static class PhoneValueNormalizer
{
    private static readonly Regex PhonePattern = new(
        @"(?<![0-9+])(?:(?:\+48|0048|48)[\s().-]*)?(?:[0-9][\s().-]*){8}[0-9](?![0-9])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length <= 32) return value;

        // Recover the first Polish phone number, not the appended business description.
        Match match;
        try
        {
            match = PhonePattern.Match(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
        if (!match.Success) return null;

        var digits = string.Concat(match.Value.Where(char.IsAsciiDigit));
        if (digits.Length == 13 && digits.StartsWith("0048", StringComparison.Ordinal))
            return "+" + digits[2..];
        return digits.Length == 11 ? "+" + digits : digits;
    }
}
