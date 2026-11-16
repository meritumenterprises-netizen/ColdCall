using AdjustCeidgAddress.Data;
using ImportCEIDGByDate.Geoportal;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Adresy = AdjustCeidgAddress.Data.FirmyTemp.Adresy;
using Company = AdjustCeidgAddress.Data.FirmyTemp.Company;
using CompanyAddress = AdjustCeidgAddress.Data.FirmyTemp.CompanyAddress;

namespace AdjustCeidgAddress;

public sealed class CompanyGeoAddressUpdater(FirmyTempDbContext context, GeoportalAddressService geoportal)
{
    private static readonly SemaphoreSlim addressWriteGate = new(1, 1);

    public async Task<bool> UpdateAsync(Company company, CancellationToken token = default)
    {
        if (company.BlockedByRodo)
        {
            Console.WriteLine($"GeoPortal NIP={company.Nip}: pominieto firme z blokada RODO.");
            return false;
        }

        var province = await context.Wojewodztwas.AsNoTracking()
            .Where(row => row.Id == company.WojewodztwoId).Select(row => row.Name).FirstOrDefaultAsync(token);
        var county = await context.Powiaties.AsNoTracking()
            .Where(row => row.Id == company.PowiatId).Select(row => row.Name).FirstOrDefaultAsync(token);
        var municipality = await context.Gminies.AsNoTracking()
            .Where(row => row.Id == company.GminaId).Select(row => row.Name).FirstOrDefaultAsync(token);
        var city = await context.Miejscowoscis.AsNoTracking()
            .Where(row => row.Id == company.MiejscowoscId).Select(row => row.Name).FirstOrDefaultAsync(token);
        // This repair explicitly verifies addresses previously skipped because ProvinceId was 1.
        var result = await geoportal.FindAsync(new GeoportalAddressRequest
        {
            CompanyId = company.Id,
            ProvinceId = company.WojewodztwoId ?? 0,
            CountyId = company.PowiatId ?? 0,
            MunicipalityId = company.GminaId ?? 0,
            CityId = company.MiejscowoscId ?? 0,
            Province = province ?? "",
            County = county ?? "",
            Municipality = municipality ?? "",
            City = city ?? "",
            Street = company.Street,
            BuildingNumber = company.BuildingNumber,
            PostalCode = company.PostalCode
        }, token);

        Console.WriteLine($"GeoPortal NIP={company.Nip}: {result.Reason}");
        if (!result.Found || result.ErrorCode != GeoportalAddressErrorCode.None)
            return false;

        // Different companies may resolve to the same source key. Commit before another worker checks it.
        await addressWriteGate.WaitAsync(token);
        try
        {
            await ApplyAsync(company, result, token);
            company.AddressSort = CompanyAddressSort.Build(company, city ?? "");
            await context.SaveChangesAsync(token);
            return true;
        }
        finally { addressWriteGate.Release(); }
    }

    private async Task ApplyAsync(Company company, GeoportalAddressResult result, CancellationToken token)
    {
        var address = await context.Adresies.FirstOrDefaultAsync(row => row.PrngId == result.SourceKey, token);
        if (address == null)
        {
            address = new Adresy
            {
                WojewodztwoId = company.WojewodztwoId!.Value,
                PowiatId = company.PowiatId!.Value,
                GminaId = company.GminaId!.Value,
                MiejscowoscId = company.MiejscowoscId!.Value,
                Ulica = result.Street,
                NrUlicy = result.BuildingNumber,
                Rodzaj = result.IsCity ? "miejscowosc UUG (GPS przyblizony)" : "punkt adresowy UUG",
                Latitude = result.Latitude,
                Longitude = result.Longitude,
                Location = new Point((double)result.Longitude, (double)result.Latitude) { SRID = 4326 },
                PrngId = result.SourceKey
            };
            context.Adresies.Add(address);
        }

        var link = await context.CompanyAddresses.OrderBy(row => row.CompanyAdresId)
            .FirstOrDefaultAsync(row => row.CompanyId == company.Id, token);
        if (link == null)
            context.CompanyAddresses.Add(new CompanyAddress { CompanyId = company.Id, Address = address });
        else
            link.Address = address;

        if (!result.IsCity)
        {
            company.Street = result.Street;
            company.BuildingNumber = result.BuildingNumber;
        }
        company.AddressIsMissing = result.IsCity;
        company.SkipAddressVerification = false;
    }
}
