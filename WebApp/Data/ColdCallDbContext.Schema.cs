using ColdCall.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace ColdCall.Data;

public partial class ColdCallDbContext
{
    public DbSet<Adres> Adresy => Set<Adres>();
    public DbSet<CompanyUserNote> CompanyUserNotes => Set<CompanyUserNote>();
    public DbSet<CsvExport> CsvExports => Set<CsvExport>();
    public DbSet<CompanyAddress> CompanyAddresses => Set<CompanyAddress>();
    public DbSet<AdresView> AddressView => Set<AdresView>();
    public DbSet<CompanyAddressView> CompanyAddressesView => Set<CompanyAddressView>();
    public DbSet<CompanyView> CompaniesView => Set<CompanyView>();
    public DbSet<CompanyWithPkdsView> CompaniesWithPkdsView => Set<CompanyWithPkdsView>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Keep only the indexes that actually exist in the database.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(options => options.UseNetTopologySuite());
    }

    private static void ConfigureDatabaseSchema(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CsvExport>(entity =>
        {
            entity.ToTable("CSVExport", "ColdCall");
            entity.Property(x => x.SearchProfileHash).HasColumnType("nvarchar(32)")
                .HasMaxLength(32).IsUnicode().IsRequired(false);
            entity.Property(x => x.NumberOfRecords).HasColumnType("int").IsRequired(false);
            entity.HasKey(x => x.EksportCsvId).HasName("PK_CSVExport").IsClustered();
            entity.Property(x => x.EksportCsvId).HasColumnName("EksportCSVId").UseIdentityColumn(1, 1);
            entity.Property(x => x.SearchProfileId).HasColumnType("int").IsRequired(false);
            entity.HasOne(x => x.SearchProfile).WithMany().HasForeignKey(x => x.SearchProfileId)
                .HasConstraintName("FK_CSVExport_SearchProfiles").OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.CreatedOn).HasColumnType("datetime").IsRequired().HasDefaultValueSql("GETDATE()");
            entity.Property(x => x.CsvFileNameId).HasColumnName("CSVFileNameId").HasColumnType("uniqueidentifier")
                .IsRequired().HasDefaultValueSql("NEWID()");
            entity.Property(x => x.CsvFileName).HasColumnName("CSVFileName").HasColumnType("nvarchar(2048)")
                .HasMaxLength(2048).IsUnicode().IsRequired();
        });
        modelBuilder.Entity<CompanyUserNote>(entity =>
        {
            entity.ToTable("CompanyUserNotes");
            entity.HasKey(x => x.Id).HasName("PK_CompanyUserNotes");
            entity.Property(x => x.Id).HasColumnName("ID").UseIdentityColumn();
            entity.Property(x => x.CompanyId).HasColumnName("CompanyID");
            entity.Property(x => x.Notes).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique()
                .HasDatabaseName("UX_CompanyUserNotes_CompanyID_UserId");
            entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId)
                .HasConstraintName("FK_CompanyUserNotes_Companies").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .HasConstraintName("FK_CompanyUserNotes_Users").OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<AppUser>().Property(x => x.GoogleEmail).HasMaxLength(450);
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users", "ColdCall");
            entity.HasKey(x => x.Id).HasName("PK_Users").IsClustered();
            entity.Property(x => x.Id).UseIdentityColumn(1, 1);
            entity.Property(x => x.GoogleSubject).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.GoogleEmail).HasColumnType("nvarchar(450)").HasMaxLength(450).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
            entity.Property(x => x.LastLoginAtUtc).HasColumnType("datetime2").IsRequired();
            entity.HasIndex(x => x.GoogleEmail).HasDatabaseName("IX_Users_GoogleEmail").IsUnique().IsClustered(false);
        });
        modelBuilder.Entity<AppUser>().Property(x => x.IsAdmin).HasColumnType("bit").IsRequired()
            .HasDefaultValue(false);
        modelBuilder.Entity<AppUser>().Property(x => x.IsEnabled).HasColumnType("bit").IsRequired()
            .HasDefaultValue(true).HasSentinel(true);
        modelBuilder.Entity<AppUser>().Property(x => x.UsesGmail).HasColumnType("bit").IsRequired()
            .HasDefaultValue(true).HasSentinel(true);
        modelBuilder.Entity<AdminEmail>().Property(x => x.Email).HasMaxLength(450);
        modelBuilder.Entity<BusinessType>().Property(x => x.TypDzialalnosciId).ValueGeneratedNever();
        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(x => x.BlockedByRODO).HasColumnType("bit").IsRequired().HasDefaultValue(false);
            entity.Property(x => x.SkipAddressVerification).HasColumnType("bit").IsRequired().HasDefaultValue(false);
            entity.Property(x => x.DataModifiedOn).HasColumnType("datetime").IsRequired(false);
            entity.Property(x => x.Krs).HasMaxLength(450);
            entity.Property(x => x.Nip).HasMaxLength(450);
            entity.Property(x => x.Regon).HasMaxLength(50);
            entity.HasIndex(x => x.Id).HasDatabaseName("IX_PrgImporter_Companies_MissingAddress")
                .HasFilter("([AddressIsMissing]=(1) AND [BlockedByRODO]=(0))")
                .IncludeProperties(x => new { x.MiejscowoscId, x.WojewodztwoId, x.PowiatId });
            entity.HasIndex(x => x.StatusDzialalnosciId);
            entity.HasOne(x => x.BusinessType).WithMany().HasForeignKey(x => x.BusinessTypeId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Wojewodztwo).WithMany().HasForeignKey(x => x.WojewodztwoId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Powiat).WithMany().HasForeignKey(x => x.PowiatId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Gmina).WithMany().HasForeignKey(x => x.GminaId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Miejscowosc).WithMany().HasForeignKey(x => x.MiejscowoscId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<PkdCode>().Property(x => x.Code).HasMaxLength(450);
        modelBuilder.Entity<PkdCode>().Property(x => x.CodeWithoutDots).HasMaxLength(4000).IsRequired(false);
        modelBuilder.Entity<CompanyPkd>().HasIndex(x => x.PkdCodeId);
        modelBuilder.Entity<SearchProfilePkd>().HasIndex(x => x.PkdCodeId);
        modelBuilder.Entity<SearchProfile>(entity =>
        {
            entity.ToTable("SearchProfiles", "ColdCall");
            entity.Property(x => x.SearchProfileHash).HasColumnType("nvarchar(1024)")
                .HasMaxLength(1024).IsUnicode().IsRequired(false);
            entity.HasKey(x => x.Id).HasName("PK_SearchProfiles").IsClustered();
            entity.Property(x => x.Id).UseIdentityColumn(1, 1);
            entity.Property(x => x.Name).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
            entity.HasIndex(x => x.AppUserId).HasDatabaseName("IX_SearchProfiles_AppUserId").IsClustered(false);
            entity.HasOne(x => x.AppUser).WithMany(x => x.SearchProfiles).HasForeignKey(x => x.AppUserId)
                .HasConstraintName("FK_SearchProfiles_Users_AppUserId").OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserPlan>().HasIndex(x => x.AppUserId);
        modelBuilder.Entity<UserPlan>().Property(x => x.IsActive).HasColumnType("bit").IsRequired(false);
        modelBuilder.Entity<UserPlan>().HasIndex(x => x.PlanDefinitionId);
        modelBuilder.Entity<Adres>(entity =>
        {
            entity.ToTable("Adresy");
            entity.Property(x => x.Ulica).HasMaxLength(200);
            entity.Property(x => x.NrUlicy).HasMaxLength(50);
            entity.Property(x => x.Rodzaj).HasMaxLength(200);
            entity.Property(x => x.PrngId).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.Location).HasColumnType("geography").IsRequired(false);
            entity.HasIndex(x => x.PrngId).HasDatabaseName("UX_Adresy_PrngId")
                .IsUnique().HasFilter("([PrngId] IS NOT NULL)");
            entity.HasOne(x => x.Wojewodztwo).WithMany().HasForeignKey(x => x.WojewodztwoId)
                .HasConstraintName("FK_Adresy_Wojewodztwa").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Powiat).WithMany().HasForeignKey(x => x.PowiatId)
                .HasConstraintName("FK_Adresy_Powiaty").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Gmina).WithMany().HasForeignKey(x => x.GminaId)
                .HasConstraintName("FK_Adresy_Gminy").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Miejscowosc).WithMany().HasForeignKey(x => x.MiejscowoscId)
                .HasConstraintName("FK_Adresy_Miejscowosci").OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CompanyAddress>(entity =>
        {
            entity.ToTable("CompanyAddress");
            entity.HasKey(x => x.CompanyAdresId).HasName("PK_ColdCall.CompanyAddress");
            entity.Property(x => x.CompanyAdresId).UseIdentityColumn();
            entity.HasIndex(x => x.CompanyId).HasDatabaseName("IX_PrgImporter_CompanyAddress_CompanyId")
                .IncludeProperties(x => x.AddressId);
            entity.HasOne(x => x.Company).WithMany(x => x.CompanyAddresses).HasForeignKey(x => x.CompanyId)
                .HasConstraintName("FK_CompanyAddress_Companies").OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Address).WithMany(x => x.CompanyAddresses).HasForeignKey(x => x.AddressId)
                .HasConstraintName("FK_CompanyAddress_Adresy").OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CompanyShortView>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(1000);
            entity.Property(x => x.Nip).HasMaxLength(450);
            entity.Property(x => x.StatusDzialalnosci).HasMaxLength(100);
            entity.Property(x => x.TypDzialalnosci).HasMaxLength(512).IsUnicode(false);
        });
        modelBuilder.Entity<AdresView>(entity =>
        {
            entity.HasNoKey().ToView("viewAdresy", "dbo");
            entity.Property(x => x.Location).HasColumnType("geography");
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.Wojewodztwo).HasMaxLength(250);
            entity.Property(x => x.Powiat).HasMaxLength(250);
            entity.Property(x => x.Gmina).HasMaxLength(250);
            entity.Property(x => x.Miejscowosc).HasMaxLength(250);
        });
        modelBuilder.Entity<CompanyAddressView>(entity =>
        {
            entity.HasNoKey().ToView("viewCompaniesAdresy", "dbo");
            entity.Property(x => x.Company).HasMaxLength(1000);
            entity.Property(x => x.MiejscowoscId).HasColumnName("MiejscowoscID");
            entity.Property(x => x.Wojewodztwo).HasMaxLength(250);
            entity.Property(x => x.Powiat).HasMaxLength(250);
            entity.Property(x => x.Gmina).HasMaxLength(250);
            entity.Property(x => x.Miejscowosc).HasMaxLength(250);
        });
        modelBuilder.Entity<CompanyView>(entity =>
        {
            entity.HasNoKey().ToView("viewCompanies", "dbo");
            entity.Property(x => x.Name).HasMaxLength(1000);
            entity.Property(x => x.TypDzialalnosci).HasMaxLength(512).IsUnicode(false);
            entity.Property(x => x.Krs).HasMaxLength(450);
            entity.Property(x => x.Nip).HasMaxLength(450);
            entity.Property(x => x.Regon).HasMaxLength(50);
            entity.Property(x => x.StatusDzialalnosci).HasMaxLength(100);
            entity.Property(x => x.Wojewodztwo).HasColumnName("Wojew\u00f3dztwo").HasMaxLength(250);
            entity.Property(x => x.Miejscowosc).HasColumnName("Miejscowo\u015b\u0107").HasMaxLength(250);
            entity.Property(x => x.Gmina).HasMaxLength(250);
            entity.Property(x => x.Powiat).HasMaxLength(250);
        });
        modelBuilder.Entity<CompanyWithPkdsView>(entity =>
        {
            entity.HasNoKey().ToView("viewCompaniesWithPKDs", "dbo");
            entity.Property(x => x.Name).HasMaxLength(1000);
            entity.Property(x => x.TypDzialalnosci).HasMaxLength(512).IsUnicode(false);
            entity.Property(x => x.Nip).HasMaxLength(450);
            entity.Property(x => x.Regon).HasMaxLength(50);
            entity.Property(x => x.StatusDzialalnosci).HasMaxLength(100);
            entity.Property(x => x.PkdCode).HasColumnName("PKDCode").HasMaxLength(450);
            entity.Property(x => x.PkdOpis).HasColumnName("PKDOpis");
            entity.Property(x => x.Wojewodztwo).HasColumnName("Wojew\u00f3dztwo");
            entity.Property(x => x.Miejscowosc).HasColumnName("Miejscowo\u015b\u0107");
        });
    }
}
