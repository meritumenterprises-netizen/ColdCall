using ColdCall.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace ColdCall.Data;

public partial class ColdCallDbContext
{
    public DbSet<Adres> Adresy => Set<Adres>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<UserLogin> UserLogins => Set<UserLogin>();
    public DbSet<CompanyXml> CompanyXml => Set<CompanyXml>();
    public DbSet<CompanyUserNote> CompanyUserNotes => Set<CompanyUserNote>();
    public DbSet<CsvExport> CsvExports => Set<CsvExport>();
    public DbSet<CsvDeletionHistory> CsvDeletionHistory => Set<CsvDeletionHistory>();
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
        ConfigureNameGramSchema(modelBuilder);
        modelBuilder.Entity<CsvDeletionHistory>(entity =>
        {
            entity.ToTable("CsvDeletionHistory", "ColdCall");
            entity.HasKey(row => row.Id).HasName("PK_CsvDeletionHistory");
            entity.Property(row => row.Id).UseIdentityColumn();
            entity.Property(row => row.DeletedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(row => row.QuotaDate).HasColumnType("date");
            entity.HasIndex(row => new { row.UserId, row.QuotaDate }).HasDatabaseName("IX_CsvDeletionHistory_UserId_QuotaDate");
        });
        modelBuilder.Entity<UserLogin>(entity =>
        {
            entity.ToTable("UserLogin", "ColdCall");
            entity.HasKey(e => e.UserLoginId).HasName("PK_UserLogin");
            entity.Property(e => e.UserLoginId).UseIdentityColumn();
            entity.Property(e => e.LoginDate).HasColumnType("datetime");
            entity.Property(e => e.IpAddress).HasMaxLength(50).IsUnicode(false).IsRequired(false);
            entity.Property(e => e.DnsAddress).HasMaxLength(2048).IsUnicode(false).IsRequired(false);
        });
        modelBuilder.Entity<GlobalSetting>(entity =>
        {
            entity.Property(e => e.AppVersion).HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
            entity.ToTable("GlobalSettings", "ColdCall");
            entity.HasKey(e => e.GlobalSettingsId).HasName("PK_GlobalSettings");
            entity.Property(e => e.GlobalSettingsId).UseIdentityColumn();
            entity.Property(e => e.ApplicationInService).HasColumnType("bit").HasDefaultValue(false);
            entity.Property(e => e.InFaktInvoicing).HasColumnType("bit").HasDefaultValue(false);
            entity.Property(e => e.RegistrationSuspended).HasColumnType("bit").HasDefaultValue(false);
            entity.Property(e => e.Regulamin).HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(e => e.RegulaminBeta).HasColumnType("nvarchar(max)").IsRequired(false);
        });
        modelBuilder.Entity<CompanyXml>(entity =>
        {
            entity.ToTable("CompanyXml", "ColdCall");
            entity.HasKey(x => x.CompanyXmlId).HasName("PK_CompanyXml").IsClustered();
            entity.Property(x => x.CompanyXmlId).UseIdentityColumn();
            entity.Property(x => x.Nip).HasMaxLength(32).IsRequired(false);
            entity.Property(x => x.XmlContent).HasColumnName("CompanyXml").HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Md5).HasColumnName("MD5").HasMaxLength(128).IsUnicode(false).IsRequired();
            entity.HasIndex(x => x.Nip).HasDatabaseName("IX_CompanyXml_NIP");
            // An alternate key would incorrectly make nullable NIP required in EF.
            entity.HasIndex(x => x.Nip, "UQ_CompanyXml_Nip")
                .HasDatabaseName("UQ_CompanyXml_Nip").IsUnique().HasFilter(null);
        });
        modelBuilder.Entity<StripePayment>(entity =>
        {
            entity.ToTable("StripePayments", "ColdCall");
            entity.HasKey(p => p.StripeSessionId).HasName("PK_StripePayments");
            entity.Property(p => p.StripeSessionId).HasMaxLength(255).ValueGeneratedNever();
            entity.Property(p => p.PaidAtUtc).HasColumnType("datetime2");
            entity.Property(p => p.AmountPaid).HasColumnType("decimal(18,2)");
            entity.Property(p => p.PDFInvoice).HasColumnType("image").IsRequired(false);
            entity.Property(p => p.InvoiceId).HasColumnType("nvarchar(128)").HasMaxLength(128).IsRequired(false);
            entity.Property(p => p.ClientId).HasColumnType("int").IsRequired(false);
            entity.Property(p => p.InvoiceNumber).HasColumnType("varchar(128)").HasMaxLength(128).IsUnicode(false).IsRequired(false);
            entity.HasIndex(p => p.AppUserId).HasDatabaseName("IX_StripePayments_AppUserId");
            entity.HasOne(p => p.AppUser).WithMany().HasForeignKey(p => p.AppUserId)
                .HasConstraintName("FK_StripePayments_Users").OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.PlanDefinition).WithMany().HasForeignKey(p => p.PlanDefinitionId)
                .HasConstraintName("FK_StripePayments_PlanDefinitions").OnDelete(DeleteBehavior.NoAction);
        });
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
            entity.Property(x => x.Phone).HasMaxLength(32).IsRequired(false);
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired(false);
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired(false);
            entity.Property(x => x.CompanyName).HasMaxLength(200).IsRequired(false);
            entity.Property(x => x.Nip).HasMaxLength(10).IsRequired(false);
            entity.Property(x => x.TermsOfServiceAccepted).HasColumnType("bit").IsRequired(false);
            entity.Property(x => x.IsBetaUser).HasColumnType("bit");
            entity.Property(x => x.PostalCode).HasMaxLength(20).IsRequired(false);
            entity.Property(x => x.City).HasMaxLength(100).IsRequired(false);
            entity.Property(x => x.Street).HasMaxLength(200).IsRequired(false);
            entity.Property(x => x.StreetNumber).HasMaxLength(50).IsRequired(false);
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
        modelBuilder.Entity<AppUser>().Property(x => x.DisablePlanPurchase).HasColumnType("bit").IsRequired()
            .HasDefaultValue(false);
        modelBuilder.Entity<BusinessType>().Property(x => x.TypDzialalnosciId).ValueGeneratedNever();
        modelBuilder.Entity<BusinessType>().Property(x => x.TypDzialalnosciSkrot).HasMaxLength(128);
        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies", "ColdCall", table =>
            {
                table.HasTrigger("TR_Companies_NameGrams");
                table.UseSqlOutputClause(false);
            });
            entity.Property(x => x.BlockedByRODO).HasColumnType("bit").IsRequired().HasDefaultValue(false);
            entity.Property(x => x.SkipAddressVerification).HasColumnType("bit").IsRequired().HasDefaultValue(false);
            entity.Property(x => x.DataModifiedOn).HasColumnType("datetime").IsRequired(false);
            entity.Property(x => x.SuspendedOn).HasColumnType("datetime").IsRequired(false);
            entity.Property(x => x.ResumedOn).HasColumnType("datetime").IsRequired(false);
            entity.Property(x => x.ImportedOn).HasColumnType("datetime").IsRequired(false);
            entity.HasIndex(x => new { x.WojewodztwoId, x.PowiatId, x.GminaId, x.MiejscowoscId, x.StatusDzialalnosciId })
                .HasDatabaseName("IX_Companies_Location_Status").IncludeProperties(nameof(Company.StartedAt));
            entity.HasIndex(x => x.ImportedOn).HasDatabaseName("IX_Companies_ImportedOn");
            entity.HasIndex(x => new { x.StatusDzialalnosciId, x.ImportedOn, x.Id })
                .HasDatabaseName("IX_Companies_Status_ImportedOn_Id").IncludeProperties(nameof(Company.StartedAt));
            entity.HasIndex(x => new { x.StatusDzialalnosciId, x.DataModifiedOn, x.Id })
                .HasDatabaseName("IX_Companies_Status_DataModifiedOn_Id").IncludeProperties(nameof(Company.StartedAt));
            entity.Property<string>("AddressSort").HasMaxLength(800);
            entity.HasIndex("AddressSort", nameof(Company.Id)).HasDatabaseName("IX_Companies_AddressSort_Id")
                .IncludeProperties(nameof(Company.StatusDzialalnosciId));
            entity.HasIndex(new[] { "AddressSort", nameof(Company.Id) }, "IX_Companies_AddressSortDesc_Id")
                .IsDescending(true, false).IncludeProperties(nameof(Company.StatusDzialalnosciId));
            entity.HasIndex(x => new { x.StartedAt, x.Id }).HasDatabaseName("IX_Companies_StartedAt_Id")
                .IsDescending(true, false).IncludeProperties(nameof(Company.WojewodztwoId));
            entity.Property(x => x.BatchImported).HasColumnType("bit").IsRequired().HasDefaultValue(false);
            entity.Property(x => x.FirstName).HasMaxLength(128);
            entity.Property(x => x.LastName).HasMaxLength(128);
            entity.Property(x => x.Krs).HasMaxLength(32);
            entity.Property(x => x.Nip).HasMaxLength(32);
            entity.Property(x => x.PostalCode).HasMaxLength(8);
            entity.Property(x => x.Street).HasMaxLength(1024);
            entity.Property(x => x.BuildingNumber).HasMaxLength(32);
            entity.Property(x => x.ApartmentNumber).HasMaxLength(32);
            entity.Property(x => x.Phone).HasMaxLength(32);
            entity.Property<byte>("ContactFlags").HasComputedColumnSql(
                "CONVERT(tinyint, (CASE WHEN [Phone] IS NOT NULL AND LTRIM(RTRIM([Phone])) <> '' THEN 1 ELSE 0 END) + (CASE WHEN [Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> '' THEN 2 ELSE 0 END) + (CASE WHEN [Website] IS NOT NULL AND LTRIM(RTRIM([Website])) <> '' THEN 4 ELSE 0 END))", stored: false);
            entity.HasIndex("ContactFlags", nameof(Company.StatusDzialalnosciId), nameof(Company.StartedAt), nameof(Company.Id))
                .HasDatabaseName("IX_Companies_ContactFlags_Status_StartedAt_Id")
                .IsDescending(false, false, true, false).IncludeProperties(nameof(Company.ImportedOn));
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasIndex(x => x.Phone).HasDatabaseName("IX_Companies_Phone").IsUnique(false).IsClustered(false);
            entity.HasIndex(x => new { x.Phone, x.Id }).HasDatabaseName("IX_Companies_PhoneDesc_Id")
                .IsDescending(true, false).IncludeProperties(nameof(Company.StatusDzialalnosciId)).IsClustered(false);
            entity.HasIndex(x => x.Email).HasDatabaseName("IX_Companies_Email").IsUnique(false).IsClustered(false);
            entity.HasIndex(x => new { x.Email, x.Id }).HasDatabaseName("IX_Companies_EmailDesc_Id")
                .IsDescending(true, false).IncludeProperties(nameof(Company.StatusDzialalnosciId)).IsClustered(false);
            entity.Property(x => x.GoogleEmail).HasMaxLength(256);
            entity.Property(x => x.Website).HasMaxLength(512);
            entity.HasIndex(x => x.Website).HasDatabaseName("IX_Companies_Website").IsUnique(false).IsClustered(false);
            entity.HasIndex(x => new { x.Website, x.Id }).HasDatabaseName("IX_Companies_WebsiteDesc_Id")
                .IsDescending(true, false).IncludeProperties(nameof(Company.StatusDzialalnosciId)).IsClustered(false);
            entity.Property(x => x.InstagramAccount).HasMaxLength(512);
            entity.Property(x => x.FacebookAccount).HasMaxLength(512);
            entity.Property(x => x.StartedAt).HasColumnType("datetime2");
            entity.Property(x => x.Regon).HasMaxLength(50);
            // Preserve nullable NIP; an alternate key would make it required in EF.
            entity.HasIndex(x => x.Nip, "UQ_Companies_Nip")
                .HasDatabaseName("UQ_Companies_Nip").IsUnique().HasFilter(null);
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
        modelBuilder.Entity<PkdCode>().Property(x => x.NeedsClarification).HasDefaultValue(false);
        modelBuilder.Entity<PkdCode>().Property(x => x.CodeWithoutDots).HasMaxLength(4000).IsRequired(false);
        modelBuilder.Entity<CompanyPkd>().HasIndex(x => x.PkdCodeId);
        modelBuilder.Entity<CompanyPkd>().HasIndex(x => new { x.PkdCodeId, x.CompanyId })
            .HasDatabaseName("IX_CompanyPkds_PkdCodeId_CompanyId");
        modelBuilder.Entity<CompanyPkd>().Property(x => x.Prevailing)
            .HasColumnType("bit").IsRequired().HasDefaultValue(false);
        modelBuilder.Entity<SearchProfilePkd>().HasIndex(x => x.PkdCodeId);
        modelBuilder.Entity<SearchProfile>(entity =>
        {
            entity.ToTable("SearchProfiles", "ColdCall");
            entity.Property(x => x.LastSavedAtUtc).HasColumnType("datetime2").IsRequired(false);
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
        modelBuilder.Entity<UserPlan>().Property(x => x.Source).HasColumnType("nvarchar(max)").IsRequired(false);
        modelBuilder.Entity<UserPlan>().HasIndex(x => x.PlanDefinitionId);
        modelBuilder.Entity<Adres>(entity =>
        {
            entity.ToTable("Adresy");
            entity.Property(x => x.Ulica).HasMaxLength(200);
            entity.Property(x => x.NrUlicy).HasMaxLength(50);
            entity.Property(x => x.Rodzaj).HasMaxLength(200);
            entity.Property(x => x.PrngId).HasMaxLength(50).IsUnicode(false);
            entity.HasIndex(x => x.PrngId).HasDatabaseName("IX_Adresy_PrngId");
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.Location).HasColumnType("geography").IsRequired(false);
            // SQL Server spatial indexes are maintained by Sql/AdresySpatialIndex.sql, not HasIndex.
            entity.HasIndex(x => new { x.MiejscowoscId, x.WojewodztwoId, x.PowiatId, x.GminaId })
                .HasDatabaseName("IX_Adresy_LocalityCenter")
                .IncludeProperties(x => new { x.Latitude, x.Longitude });
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
            entity.HasIndex(x => new { x.AddressId, x.CompanyId })
                .HasDatabaseName("IX_CompanyAddress_AddressId_CompanyId");
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
