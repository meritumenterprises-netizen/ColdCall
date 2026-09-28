using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Data;

public class ColdCallDbContext : DbContext
{
    public ColdCallDbContext(DbContextOptions<ColdCallDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AdminEmail> AdminEmails => Set<AdminEmail>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<PkdCode> PkdCodes => Set<PkdCode>();
    public DbSet<CompanyPkd> CompanyPkds => Set<CompanyPkd>();
    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<SearchProfilePkd> SearchProfilePkds => Set<SearchProfilePkd>();
    public DbSet<PlanDefinition> PlanDefinitions => Set<PlanDefinition>();
    public DbSet<UserPlan> UserPlans => Set<UserPlan>();
    public DbSet<Wojewodztwo> Wojewodztwa => Set<Wojewodztwo>();
    public DbSet<Miejscowosc> Miejscowosci => Set<Miejscowosc>();
    public DbSet<Gmina> Gminy => Set<Gmina>();
    public DbSet<Powiat> Powiaty => Set<Powiat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.GoogleEmail).IsUnique();
        modelBuilder.Entity<AdminEmail>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<PkdCode>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Company>().HasIndex(x => x.Nip);
        modelBuilder.Entity<Company>().HasIndex(x => x.Krs);
        modelBuilder.Entity<CompanyPkd>().HasKey(x => new { x.CompanyId, x.PkdCodeId });
        modelBuilder.Entity<SearchProfilePkd>().HasKey(x => new { x.SearchProfileId, x.PkdCodeId });
        modelBuilder.Entity<PlanDefinition>().Property(x => x.Price).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<UserPlan>().Property(x => x.PricePaid).HasColumnType("decimal(18,2)");
    }
}

public static class DbSeeder
{
    public static async Task SeedAsync(ColdCallDbContext db)
    {
        if (!db.PlanDefinitions.Any())
        {
            db.PlanDefinitions.AddRange(
                new PlanDefinition("PX1", 50, 499),
                new PlanDefinition("PX2", 20, 299),
                new PlanDefinition("PX3", 10, 199),
                new PlanDefinition("PX4", 3, 99),
                new PlanDefinition("PX5", 1, 49));
        }

        if (!db.AdminEmails.Any(x => x.Email == "pluczak99@gmail.com"))
        {
            db.AdminEmails.Add(new AdminEmail { Email = "pluczak99@gmail.com", AddedByEmail = "system" });
        }

        if (!db.PkdCodes.Any())
        {
            db.PkdCodes.AddRange(
                new PkdCode { Code = "62.01.Z", Name = "Dzialalnosc zwiazana z oprogramowaniem" },
                new PkdCode { Code = "70.22.Z", Name = "Doradztwo w zakresie prowadzenia dzialalnosci gospodarczej" },
                new PkdCode { Code = "46.90.Z", Name = "Sprzedaz hurtowa niewyspecjalizowana" },
                new PkdCode { Code = "41.20.Z", Name = "Roboty budowlane zwiazane ze wznoszeniem budynkow" });
        }

        if (!db.Wojewodztwa.Any())
        {
            db.Wojewodztwa.AddRange(
                new Wojewodztwo { Name = "dolnoslaskie" },
                new Wojewodztwo { Name = "mazowieckie" },
                new Wojewodztwo { Name = "slaskie" },
                new Wojewodztwo { Name = "wielkopolskie" });
        }

        await db.SaveChangesAsync();
    }
}
