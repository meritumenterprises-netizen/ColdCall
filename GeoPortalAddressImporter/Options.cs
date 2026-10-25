internal sealed class Options
{
    public bool Apply { get; private set; }
    public bool Restart { get; private set; }
    public bool RetryErrors { get; private set; }
    public int Limit { get; private set; } = 10;
    public int? CompanyId { get; private set; }
    public int? AfterId { get; private set; }
    public int? DelayMilliseconds { get; private set; }
    public string? DataDirectory { get; private set; }

    public static Options Parse(string[] args)
    {
        var options = new Options();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!seen.Add(key)) throw new ArgumentException("Powtorzony argument: " + key);
            string Value()
            {
                if (++i >= args.Length) throw new ArgumentException("Brak wartosci: " + key);
                return args[i];
            }
            int Number(bool zero = false)
            {
                if (!int.TryParse(Value(), out var number) || number < (zero ? 0 : 1))
                    throw new ArgumentException("Nieprawidlowa liczba: " + key);
                return number;
            }
            switch (key)
            {
                case "--apply": options.Apply = true; break;
                case "--restart": options.Restart = true; break;
                case "--retry-errors": options.RetryErrors = true; break;
                case "--all": options.Limit = int.MaxValue; break;
                case "--limit": options.Limit = Number(); break;
                case "--company-id": options.CompanyId = Number(); options.Limit = 1; break;
                case "--after-id": options.AfterId = Number(true); break;
                case "--delay-ms": options.DelayMilliseconds = Number(true); break;
                case "--data-dir": options.DataDirectory = Value(); break;
                default: throw new ArgumentException("Nieznany argument: " + key);
            }
        }
        if (seen.Contains("--limit") && seen.Contains("--all"))
            throw new ArgumentException("Wybierz --limit lub --all.");
        if (options.CompanyId != null && (seen.Contains("--limit") || seen.Contains("--all") || options.AfterId != null))
            throw new ArgumentException("--company-id nie laczy sie z --limit, --all lub --after-id.");
        if (options.RetryErrors && (options.CompanyId != null || options.AfterId != null || options.Restart))
            throw new ArgumentException("--retry-errors nie laczy sie z --company-id, --after-id ani --restart.");
        return options;
    }
}
