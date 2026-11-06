using System;
using System.Collections.Generic;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Data;

public partial class FirmyTempDbContext : DbContext
{
    public FirmyTempDbContext(DbContextOptions<FirmyTempDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Adresy> Adresies { get; set; }

    public virtual DbSet<BusinessType> BusinessTypes { get; set; }

    public virtual DbSet<Company> Companies { get; set; }
    public virtual DbSet<GlobalSetting> GlobalSettings { get; set; }
    public virtual DbSet<UserLogin> UserLogins { get; set; }
    public virtual DbSet<CompanyXml> CompanyXml { get; set; }
    public virtual DbSet<ImportByDate> ImportByDates { get; set; }
    public virtual DbSet<CompanyPkd> CompanyPkds { get; set; }

    public virtual DbSet<CompanyAddress> CompanyAddresses { get; set; }


    public virtual DbSet<CompanyStatus> CompanyStatuses { get; set; }

    public virtual DbSet<Gminy> Gminies { get; set; }

    public virtual DbSet<Miejscowosci> Miejscowoscis { get; set; }

    public virtual DbSet<PkdCode> PkdCodes { get; set; }

    public virtual DbSet<Powiaty> Powiaties { get; set; }

    public virtual DbSet<Wojewodztwa> Wojewodztwas { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureNameGramSchema(modelBuilder);
        modelBuilder.Entity<UserLogin>(entity =>
        {
            entity.ToTable("UserLogin", "ColdCall");
            entity.HasKey(e => e.UserLoginId).HasName("PK_UserLogin");
            entity.Property(e => e.UserLoginId).UseIdentityColumn();
            entity.Property(e => e.LoginDate).HasColumnType("datetime");
        });
        modelBuilder.Entity<GlobalSetting>(entity =>
        {
            entity.ToTable("GlobalSettings", "ColdCall");
            entity.HasKey(e => e.GlobalSettingsId).HasName("PK_GlobalSettings");
            entity.Property(e => e.GlobalSettingsId).UseIdentityColumn();
            entity.Property(e => e.ApplicationInService).HasColumnType("bit").HasDefaultValue(false);
            entity.Property(e => e.RegistrationSuspended).HasColumnType("bit").HasDefaultValue(false);
            entity.Property(e => e.Regulamin).HasColumnType("nvarchar(max)").IsRequired(false);
            entity.Property(e => e.RegulaminBeta).HasColumnType("nvarchar(max)").IsRequired(false);
        });
        modelBuilder.Entity<ImportByDate>(entity =>
        {
            entity.ToTable("ImportByDate", "ColdCall");
            entity.HasKey(e => e.ImportId).HasName("PK_ImportByDate");
            entity.Property(e => e.ImportId).ValueGeneratedOnAdd();
            entity.Property(e => e.ImportDate).HasColumnType("datetime");
            entity.Property(e => e.StartedAt).HasColumnType("datetime");
            entity.Property(e => e.FinishedAt).HasColumnType("datetime");
            entity.Property(e => e.Summary).HasColumnType("varchar(max)").IsRequired(false);
            entity.Property(e => e.Duration).HasMaxLength(50).IsUnicode(false).IsRequired(false);
        });

        modelBuilder.Entity<CompanyXml>(entity =>
        {
            entity.ToTable("CompanyXml", "ColdCall");
            entity.HasKey(e => e.CompanyXmlId).HasName("PK_CompanyXml");
            entity.Property(e => e.CompanyXmlId).ValueGeneratedOnAdd();
            entity.Property(e => e.Nip).HasMaxLength(32);
            entity.HasIndex(e => e.Nip, "IX_CompanyXml_NIP");
            entity.HasIndex(e => e.Nip, "UQ_CompanyXml_Nip").IsUnique().HasFilter(null);
            entity.Property(e => e.DateModified).HasColumnType("datetime");
            entity.Property(e => e.WojewodztwoId).HasColumnType("int").IsRequired(false);
            entity.Property(e => e.XmlContent).HasColumnName("CompanyXml")
                .HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(e => e.Md5).HasColumnName("MD5")
                .HasMaxLength(128).IsUnicode(false).IsRequired();
        });

        modelBuilder.Entity<Adresy>(entity =>
        {
            entity.ToTable("Adresy", "ColdCall");

            entity.HasIndex(e => new { e.MiejscowoscId, e.WojewodztwoId, e.PowiatId, e.GminaId }, "IX_Adresy_LocalityCenter");

            // SIX_Adresy_Location is a SQL Server spatial index, not an EF B-tree index.
            entity.Property(e => e.Location).HasColumnType("geography");

            entity.Property(e => e.Latitude).HasColumnType("decimal(9, 6)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(9, 6)");
            entity.Property(e => e.NrUlicy).HasMaxLength(50);
            entity.Property(e => e.PrngId)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Rodzaj).HasMaxLength(200);
            entity.Property(e => e.Ulica).HasMaxLength(200);

            entity.HasOne(d => d.Gmina).WithMany()
                .HasForeignKey(d => d.GminaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Adresy_Gminy");

            entity.HasOne(d => d.Miejscowosc).WithMany()
                .HasForeignKey(d => d.MiejscowoscId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Adresy_Miejscowosci");

            entity.HasOne(d => d.Powiat).WithMany()
                .HasForeignKey(d => d.PowiatId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Adresy_Powiaty");

            entity.HasOne(d => d.Wojewodztwo).WithMany()
                .HasForeignKey(d => d.WojewodztwoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Adresy_Wojewodztwa");
        });

        modelBuilder.Entity<BusinessType>(entity =>
        {
            entity.HasKey(e => e.TypDzialalnosciId);

            entity.ToTable("BusinessType", "ColdCall");

            entity.Property(e => e.TypDzialalnosciId).ValueGeneratedNever();
            entity.Property(e => e.TypDzialalnosci).HasMaxLength(512);
            entity.Property(e => e.TypDzialalnosciSkrot).HasMaxLength(128);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies", "ColdCall", table =>
            {
                table.HasTrigger("TR_Companies_NameGrams");
                table.UseSqlOutputClause(false);
            });

            entity.HasIndex(e => e.DataModifiedOn, "IX_Companies_DataModifiedOn");
            entity.HasIndex(e => e.ImportedOn, "IX_Companies_ImportedOn");
            entity.HasIndex(e => new { e.StatusDzialalnosciId, e.DataModifiedOn, e.Id }, "IX_Companies_Status_DataModifiedOn_Id")
                .IncludeProperties(e => e.StartedAt);

            entity.HasIndex(e => e.Nip, "IX_Companies_Nip");

            entity.HasIndex(e => new { e.StartedAt, e.Id }, "IX_Companies_StartedAt_Id").IsDescending(true, false);

            entity.HasIndex(e => e.StatusDzialalnosciId, "IX_Companies_StatusDzialalnosciId");

            entity.HasIndex(e => e.Id, "IX_PrgImporter_Companies_MissingAddress").HasFilter("([AddressIsMissing]=(1) AND [BlockedByRODO]=(0))");

            entity.HasIndex(e => e.Name, "NonClusteredIndex-20260930-225523");

            entity.HasIndex(e => e.Name, "NonClusteredIndex-20261003-065101");

            entity.HasIndex(e => e.StartedAt, "NonClusteredIndex-StartedAtASC");

            entity.HasIndex(e => e.WojewodztwoId, "NonClusteredIndex-Wojewodztwo");

            entity.Property(e => e.BlockedByRodo).HasColumnName("BlockedByRODO");
            entity.Property(e => e.CreatedOn)
                .HasDefaultValueSql("(getdate())", "DF_Companies_CreatedOn")
                .HasColumnType("datetime");
            entity.Property(e => e.DataModifiedOn).HasColumnType("datetime");
            entity.Property(e => e.ImportedOn).HasColumnType("datetime");
            entity.Property(e => e.Krs).HasMaxLength(32);
            entity.Property(e => e.Nip).HasMaxLength(32);
            entity.Property(e => e.FirstName).HasMaxLength(128);
            entity.Property(e => e.LastName).HasMaxLength(128);
            entity.Property(e => e.PostalCode).HasMaxLength(8);
            entity.Property(e => e.AddressSort).HasMaxLength(800);
            entity.HasIndex(e => new { e.AddressSort, e.Id }).HasDatabaseName("IX_Companies_AddressSort_Id")
                .IncludeProperties(e => e.StatusDzialalnosciId);
            entity.HasIndex(new[] { nameof(Company.AddressSort), nameof(Company.Id) }, "IX_Companies_AddressSortDesc_Id")
                .IsDescending(true, false).IncludeProperties(e => e.StatusDzialalnosciId);
            entity.Property(e => e.Street).HasMaxLength(1024);
            entity.Property(e => e.BuildingNumber).HasMaxLength(32);
            entity.Property(e => e.ApartmentNumber).HasMaxLength(32);
            entity.Property(e => e.Phone).HasMaxLength(32);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.GoogleEmail).HasMaxLength(256);
            entity.Property(e => e.Website).HasMaxLength(512);
            entity.Property(e => e.InstagramAccount).HasMaxLength(512);
            entity.Property(e => e.FacebookAccount).HasMaxLength(512);
            entity.Property(e => e.Name).HasMaxLength(1000);
            entity.Property(e => e.Regon).HasMaxLength(50);
            entity.Property(e => e.ResumedOn).HasColumnType("datetime");
            entity.Property(e => e.StatusDzialalnosciId).HasDefaultValue((byte)1, "DF_Companies_StatusDzialalnosciId");
            entity.Property(e => e.SuspendedOn).HasColumnType("datetime");

            entity.HasOne(d => d.BusinessTypeNavigation).WithMany(p => p.Companies)
                .HasForeignKey(d => d.BusinessType)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.Gmina).WithMany().HasForeignKey(d => d.GminaId);

            entity.HasOne(d => d.Miejscowosc).WithMany().HasForeignKey(d => d.MiejscowoscId);

            entity.HasOne(d => d.Powiat).WithMany().HasForeignKey(d => d.PowiatId);

            entity.HasOne(d => d.StatusDzialalnosci).WithMany(p => p.Companies)
                .HasForeignKey(d => d.StatusDzialalnosciId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.Wojewodztwo).WithMany().HasForeignKey(d => d.WojewodztwoId);
        });

        modelBuilder.Entity<CompanyAddress>(entity =>
        {
            entity.HasKey(e => e.CompanyAdresId).HasName("PK_ColdCall.CompanyAddress");

            entity.ToTable("CompanyAddress", "ColdCall");

            entity.HasIndex(e => e.CompanyId, "IX_PrgImporter_CompanyAddress_CompanyId");

            entity.HasOne(d => d.Address).WithMany(p => p.CompanyAddresses)
                .HasForeignKey(d => d.AddressId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CompanyAddress_Adresy");

            entity.HasOne(d => d.Company).WithMany(p => p.CompanyAddresses)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CompanyAddress_Companies");
        });

        modelBuilder.Entity<Company>()
            .HasMany(c => c.PkdCodes)
            .WithMany()
            .UsingEntity<CompanyPkd>(
                join => join.HasOne(p => p.PkdCode).WithMany().HasForeignKey(p => p.PkdCodeId)
                    .HasConstraintName("FK_CompanyPkds_PkdCodes_PkdCodeId"),
                join => join.HasOne(p => p.Company).WithMany(c => c.CompanyPkds).HasForeignKey(p => p.CompanyId)
                    .HasConstraintName("FK_CompanyPkds_Companies_CompanyId"),
                join =>
                {
                    join.ToTable("CompanyPkds", "ColdCall");
                    join.Property(p => p.Id).ValueGeneratedOnAdd();
                    join.HasKey(p => p.Id);
                    join.HasIndex("CompanyId", "PkdCodeId")
                        .HasDatabaseName("IX_CompanyPkds_CompanyId_PkdCodeId").IsUnique();
                    join.HasIndex("PkdCodeId").HasDatabaseName("IX_CompanyPkds_PkdCodeId");
                });

        modelBuilder.Entity<CompanyStatus>(entity =>
        {
            entity.HasKey(e => e.StatusId);

            entity.ToTable("CompanyStatus", "ColdCall");

            entity.Property(e => e.Nazwa).HasMaxLength(100);
        });

        modelBuilder.Entity<Gminy>(entity =>
        {
            entity.ToTable("Gminy", "ColdCall");

            entity.HasIndex(e => e.Name, "UX_Gminy_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(250);
        });

        modelBuilder.Entity<Miejscowosci>(entity =>
        {
            entity.ToTable("Miejscowosci", "ColdCall");

            entity.HasIndex(e => e.Name, "UX_Miejscowosci_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(250);
        });

        modelBuilder.Entity<PkdCode>(entity =>
        {
            entity.ToTable("PkdCodes", "ColdCall");

            entity.HasIndex(e => e.Code, "IX_PkdCodes_Code").IsUnique();

            entity.Property(e => e.CodeWithoutDots)
                .HasMaxLength(4000)
                .HasComputedColumnSql("(replace([Code],N'.',N''))", true);
        });

        modelBuilder.Entity<Powiaty>(entity =>
        {
            entity.ToTable("Powiaty", "ColdCall");

            entity.HasIndex(e => e.Name, "UX_Powiaty_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(250);
        });

        modelBuilder.Entity<Wojewodztwa>(entity =>
        {
            entity.ToTable("Wojewodztwa", "ColdCall");

            entity.HasIndex(e => e.Name, "UX_Wojewodztwa_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(250);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
