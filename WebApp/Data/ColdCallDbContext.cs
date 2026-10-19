using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Data;

public partial class ColdCallDbContext : DbContext
{
    public ColdCallDbContext(DbContextOptions<ColdCallDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AdminEmail> AdminEmails => Set<AdminEmail>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyStatus> CompanyStatuses => Set<CompanyStatus>();
    public DbSet<CompanyShortView> CompaniesShort => Set<CompanyShortView>();
    public DbSet<PkdCode> PkdCodes => Set<PkdCode>();
    public DbSet<CompanyPkd> CompanyPkds => Set<CompanyPkd>();
    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<SearchProfileCriteria> SearchProfileCriteria => Set<SearchProfileCriteria>();
    public DbSet<SearchProfilePkd> SearchProfilePkds => Set<SearchProfilePkd>();
    public DbSet<PlanDefinition> PlanDefinitions => Set<PlanDefinition>();
    public DbSet<UserPlan> UserPlans => Set<UserPlan>();
    public DbSet<StripePayment> StripePayments => Set<StripePayment>();
    public DbSet<Wojewodztwo> Wojewodztwa => Set<Wojewodztwo>();
    public DbSet<Miejscowosc> Miejscowosci => Set<Miejscowosc>();
    public DbSet<Gmina> Gminy => Set<Gmina>();
    public DbSet<Powiat> Powiaty => Set<Powiat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ColdCall");
        modelBuilder.Entity<Wojewodztwo>().HasIndex(x => x.Name)
            .HasDatabaseName("UX_Wojewodztwa_Name").IsUnique();
        modelBuilder.Entity<Powiat>().HasIndex(x => x.Name)
            .HasDatabaseName("UX_Powiaty_Name").IsUnique();
        modelBuilder.Entity<Gmina>().HasIndex(x => x.Name)
            .HasDatabaseName("UX_Gminy_Name").IsUnique();
        modelBuilder.Entity<Miejscowosc>().HasIndex(x => x.Name)
            .HasDatabaseName("UX_Miejscowosci_Name").IsUnique();
        modelBuilder.Entity<CompanyStatus>(entity =>
        {
            entity.ToTable("CompanyStatus");
            entity.HasKey(x => x.StatusId);
            entity.Property(x => x.StatusId).ValueGeneratedNever();
            entity.Property(x => x.Nazwa).HasMaxLength(100).IsRequired();
            entity.HasData(new CompanyStatus { StatusId = 1, Nazwa = "Aktywny" });
        });
        modelBuilder.Entity<Company>().Property(x => x.StatusDzialalnosciId).HasDefaultValue((byte)1);
        modelBuilder.Entity<Company>().HasOne(x => x.StatusDzialalnosci).WithMany()
            .HasForeignKey(x => x.StatusDzialalnosciId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<CompanyShortView>().HasNoKey().ToView("viewCompaniesShort", "ColdCall");

        modelBuilder.Entity<AppUser>().HasIndex(x => x.GoogleEmail).IsUnique();
        modelBuilder.Entity<AdminEmail>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<BusinessType>(entity =>
        {
            entity.ToTable("BusinessType");
            entity.HasKey(x => x.TypDzialalnosciId);
            entity.Property(x => x.TypDzialalnosci).HasColumnType("nvarchar(512)").HasMaxLength(512).IsUnicode().IsRequired();
        });
        modelBuilder.Entity<PkdCode>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<PkdCode>().Property(x => x.CodeWithoutDots)
            .HasMaxLength(450)
            .HasComputedColumnSql("REPLACE([Code], N'.', N'')", stored: true);
        modelBuilder.Entity<Company>().HasIndex(x => x.Nip)
            .HasDatabaseName("IX_Companies_Nip")
            .IsUnique(false).IsClustered(false).IsDescending(false);
        modelBuilder.Entity<Company>().Property(x => x.Name).HasMaxLength(1000).IsRequired();
        modelBuilder.Entity<Company>().Property(x => x.CreatedOn)
            .HasColumnType("datetime")
            .HasDefaultValueSql("GETDATE()");
        modelBuilder.Entity<Company>().HasIndex(x => x.Name)
            .HasDatabaseName("NonClusteredIndex-20260930-225523")
            .IsClustered(false);
        modelBuilder.Entity<Company>().HasIndex(x => x.Name, "NonClusteredIndex-20261003-065101")
            .HasDatabaseName("NonClusteredIndex-20261003-065101")
            .IsClustered(false);
        modelBuilder.Entity<Company>().HasIndex(x => x.StartedAt)
            .HasDatabaseName("NonClusteredIndex-StartedAtASC")
            .IsClustered(false);
        modelBuilder.Entity<Company>().HasIndex(x => x.DataModifiedOn)
            .HasDatabaseName("IX_Companies_DataModifiedOn")
            .IsClustered(false);
        modelBuilder.Entity<Company>().HasIndex(x => new { x.StartedAt, x.Id })
            .HasDatabaseName("IX_Companies_StartedAt_Id")
            .IsDescending(true, false)
            .IncludeProperties(x => x.WojewodztwoId)
            .IsClustered(false);
        modelBuilder.Entity<Company>().HasIndex(x => x.WojewodztwoId)
            .HasDatabaseName("NonClusteredIndex-Wojewodztwo")
            .IsClustered(false);
        modelBuilder.Entity<Company>().Property(x => x.BusinessTypeId).HasColumnName("BusinessType");
        modelBuilder.Entity<Company>()
            .HasOne(x => x.BusinessType)
            .WithMany()
            .HasForeignKey(x => x.BusinessTypeId)
            .HasPrincipalKey(x => x.TypDzialalnosciId);
        modelBuilder.Entity<CompanyPkd>().HasKey(x => x.Id);
        modelBuilder.Entity<CompanyPkd>().Property(x => x.Id).UseIdentityColumn();
        modelBuilder.Entity<CompanyPkd>().HasIndex(x => new { x.CompanyId, x.PkdCodeId })
            .HasDatabaseName("IX_CompanyPkds_CompanyId_PkdCodeId")
            .IsUnique()
            .IsDescending(false, false);
        modelBuilder.Entity<SearchProfilePkd>().HasKey(x => new { x.SearchProfileId, x.PkdCodeId });
        modelBuilder.Entity<SearchProfileCriteria>(entity =>
        {
            entity.ToTable("SearchProfileCriteria", table =>
            {
                table.HasCheckConstraint("CK_SearchProfileCriteria_SortColumn", "[SortColumn] IN (N'name',N'nip',N'fullAddress',N'phone',N'email',N'www',N'dataAktywnosci')");
                table.HasCheckConstraint("CK_SearchProfileCriteria_SortDirection", "[SortDirection] IN ('asc','desc')");
                table.HasCheckConstraint("CK_SearchProfileCriteria_RecordLimit", "[RecordLimit] BETWEEN 1 AND 10000");
                table.HasCheckConstraint("CK_SearchProfileCriteria_Dates", "([DateRangeEnabled] = 0 AND [DateFrom] IS NULL AND [DateTo] IS NULL) OR ([DateRangeEnabled] = 1 AND [DateFrom] IS NOT NULL AND [DateTo] IS NOT NULL AND [DateTo] >= [DateFrom])");
            });
            entity.Property(x => x.Id).UseIdentityColumn();
            entity.Property(x => x.SortColumn).HasMaxLength(32).HasDefaultValue("dataAktywnosci");
            entity.Property(x => x.SortDirection).HasColumnType("varchar(4)").IsUnicode(false).HasDefaultValue("desc");
            entity.Property(x => x.Address).HasMaxLength(1000).HasDefaultValue("");
            entity.Property(x => x.CompanyName).HasMaxLength(1000).HasDefaultValue("");
            entity.Property(x => x.Name).HasMaxLength(200).HasDefaultValue("").IsRequired();
            entity.Property(x => x.DateFrom).HasColumnType("date");
            entity.Property(x => x.DateTo).HasColumnType("date");
            entity.Property(x => x.DateRangeEnabled).HasDefaultValue(false);
            entity.Property(x => x.RecordLimit).HasDefaultValue(1000);
            entity.HasIndex(x => x.SearchProfileId).IsUnique();
            entity.HasOne(x => x.SearchProfile).WithOne(x => x.Criteria)
                .HasForeignKey<SearchProfileCriteria>(x => x.SearchProfileId)
                .HasConstraintName("FK_SearchProfileCriteria_SearchProfiles_SearchProfileId")
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Wojewodztwo).WithMany().HasForeignKey(x => x.WojewodztwoId)
                .HasConstraintName("FK_SearchProfileCriteria_Wojewodztwa").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Powiat).WithMany().HasForeignKey(x => x.PowiatId)
                .HasConstraintName("FK_SearchProfileCriteria_Powiaty").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Gmina).WithMany().HasForeignKey(x => x.GminaId)
                .HasConstraintName("FK_SearchProfileCriteria_Gminy").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Miejscowosc).WithMany().HasForeignKey(x => x.MiejscowoscId)
                .HasConstraintName("FK_SearchProfileCriteria_Miejscowosci").OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<PlanDefinition>().Property(x => x.DescriptionHtml).HasDefaultValue("");
        modelBuilder.Entity<PlanDefinition>().Property(x => x.Order).HasColumnType("int").IsRequired().HasDefaultValue(0);
        modelBuilder.Entity<PlanDefinition>().Property(x => x.Price).HasColumnType("decimal(18,2)");
        modelBuilder.Entity<PlanDefinition>().Property(x => x.IsTrial).HasDefaultValue(false);
        modelBuilder.Entity<PlanDefinition>().Property(x => x.IsUpgrade).HasColumnType("bit").IsRequired().HasDefaultValue(false);
        modelBuilder.Entity<PlanDefinition>().Property(x => x.UpgradeFrom).HasColumnType("int");
        modelBuilder.Entity<PlanDefinition>().Property(x => x.UpgradeTo).HasColumnType("int");
        modelBuilder.Entity<PlanDefinition>().HasOne(x => x.UpgradeToPlan).WithMany()
            .HasForeignKey(x => x.UpgradeTo)
            .HasConstraintName("FK_PlanDefinitions_PlanDefinitions1")
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PlanDefinition>().HasOne(x => x.UpgradeFromPlan).WithMany()
            .HasForeignKey(x => x.UpgradeFrom)
            .HasConstraintName("FK_PlanDefinitions_PlanDefinitions")
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<PlanDefinition>().Property(x => x.ShowInOffer).HasDefaultValue(true).HasSentinel(true);
        modelBuilder.Entity<PlanDefinition>().Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
        modelBuilder.Entity<UserPlan>().Property(x => x.PricePaid).HasColumnType("decimal(18,2)");
        ConfigureDatabaseSchema(modelBuilder);
   }
}

public static class DbSeeder
{
    public static async Task SeedAsync(ColdCallDbContext db)
    {
        if (!await db.CompanyStatuses.AnyAsync(x => x.StatusId == 1))
            db.CompanyStatuses.Add(new CompanyStatus { StatusId = 1, Nazwa = "Aktywny" });
        if (!db.PlanDefinitions.Any())
        {
            db.PlanDefinitions.AddRange(
                new PlanDefinition("PX1", 50, 499),
                new PlanDefinition("PX2", 20, 299),
                new PlanDefinition("PX3", 10, 199),
                new PlanDefinition("PX4", 3, 99),
                new PlanDefinition("PX5", 1, 49));
        }

        if (!db.BusinessTypes.Any(x => x.TypDzialalnosciId == 1))
        {
            db.BusinessTypes.Add(new BusinessType
            {
                TypDzialalnosciId = 1,
                TypDzialalnosci = "Jednoosobowa działalność gospodarcza"
            });
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
