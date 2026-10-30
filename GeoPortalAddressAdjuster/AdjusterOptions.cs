namespace GeoPortalAddressAdjuster;

public sealed class AdjusterOptions
{
    public int Workers { get; private set; } = 32;
    public int DelayMilliseconds { get; private set; } = 0;
    public int CacheDays { get; private set; } = 1;

    public static AdjusterOptions Parse(string[] args)
    {
        var options = new AdjusterOptions();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var value))
                throw new ArgumentException("Expected --workers N, --delay-ms N or --cache-days N.");
            switch (args[i])
            {
                case "--workers" when value is >= 1 and <= 128: options.Workers = value; break;
                case "--delay-ms" when value >= 0: options.DelayMilliseconds = value; break;
                case "--cache-days" when value >= 0: options.CacheDays = value; break;
                default: throw new ArgumentException("Invalid option or value: " + args[i]);
            }
        }
        return options;
    }
}
