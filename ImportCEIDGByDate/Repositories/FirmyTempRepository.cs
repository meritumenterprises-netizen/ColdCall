using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Repositories;

public sealed class FirmyTempRepository(FirmyTempDbContext context)
{
    public DbSet<Company> Companies => context.Companies;
    public DbSet<CompanyXml> CompanyXml => context.CompanyXml;
    public DbSet<Wojewodztwa> Wojewodztwa => context.Wojewodztwas;
    public DbSet<Powiaty> Powiaty => context.Powiaties;
    public DbSet<Gminy> Gminy => context.Gminies;
    public DbSet<Miejscowosci> Miejscowosci => context.Miejscowoscis;
    public DbSet<CompanyStatus> CompanyStatuses => context.CompanyStatuses;
    public DbSet<PkdCode> PkdCodes => context.PkdCodes;
    public DbSet<CompanyPkd> CompanyPkds => context.CompanyPkds;
    public DbSet<Adresy> Addresses => context.Adresies;
    public DbSet<CompanyAddress> CompanyAddresses => context.CompanyAddresses;

    public Task<Company?> FindByNipAsync(string nip, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nip);
        return Companies
            .Include(c => c.Wojewodztwo)
            .Include(c => c.Powiat)
            .Include(c => c.Gmina)
            .Include(c => c.Miejscowosc)
            .Include(c => c.StatusDzialalnosci)
            .Include(c => c.BusinessTypeNavigation)
            .Include(c => c.PkdCodes)
            .Include(c => c.CompanyPkds).ThenInclude(p => p.PkdCode)
            .Include(c => c.CompanyAddresses).ThenInclude(a => a.Address)
            .AsSplitQuery()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(c => c.Nip == nip, token);
    }

    public Task<int> SaveChangesAsync(CancellationToken token = default) =>
        context.SaveChangesAsync(token);
}
