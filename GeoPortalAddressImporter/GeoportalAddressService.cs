namespace GeoPortalAddressImporter;

// Reuses the importer's matching, verification, throttling and cache without running its CLI.
public sealed class GeoportalAddressService : IDisposable
{
    private readonly RunLog log;
    private readonly GeoportalClient client;

    public GeoportalAddressService(HttpClient httpClient, string dataDirectory)
    {
        log = new RunLog(Path.Combine(dataDirectory, "logs"));
        // Imported address changes must not be verified against yesterday's cached street list.
        client = new GeoportalClient(httpClient, Path.Combine(dataDirectory, "cache"), 100, 0, log);
    }

    public async Task<GeoportalAddressResult> FindAsync(GeoportalAddressRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProvinceId <= 1 || request.CountyId <= 0 ||
            request.MunicipalityId <= 0 || request.CityId <= 0 ||
            new[] { request.Province, request.County, request.Municipality, request.City }
                .Any(string.IsNullOrWhiteSpace))
            return new GeoportalAddressResult { Reason = "Brak pelnego podzialu administracyjnego." };

        var company = new Company(request.CompanyId, request.ProvinceId, request.CityId,
            request.Province, request.City, request.Street, request.BuildingNumber,
            request.PostalCode, request.CountyId, request.MunicipalityId,
            request.County, request.Municipality);
        MatchResult match;
        try { match = await client.FindAsync(company, token); }
        catch (GeoportalQueryException ex)
        {
            return new GeoportalAddressResult { Reason = ex.Message };
        }
        if (match.Address is not { } address)
            return new GeoportalAddressResult { Reason = match.Reason };

        var (latitude, longitude) = Coordinates.Convert(address.X, address.Y);
        return new GeoportalAddressResult
        {
            Found = true,
            Reason = match.Reason,
            Street = address.Street,
            BuildingNumber = address.IsCity ? null : address.Number,
            SourceKey = address.SourceKeyFor(company),
            IsCity = address.IsCity,
            Latitude = Math.Round((decimal)latitude, 6),
            Longitude = Math.Round((decimal)longitude, 6)
        };
    }

    public void Dispose() => log.Dispose();
}
