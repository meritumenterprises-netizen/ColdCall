using System;
using System.Collections.Generic;
using ImportCEIDGByDate.Data.FirmyImport;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Data;

public partial class FirmyImportDbContext : DbContext
{
    public FirmyImportDbContext(DbContextOptions<FirmyImportDbContext> options)
        : base(options)
    {
    }


    public virtual DbSet<ImportTarget> ImportTargets { get; set; }
    public virtual DbSet<ImportReport> ImportReports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<ImportReport>(entity =>
        {
            entity.ToTable("ImportReports", "dbo");
            entity.HasKey(report => report.ReportId).HasName("PK_ImportReports");
            entity.Property(report => report.ReportId).ValueGeneratedOnAdd();
            entity.Property(report => report.ImportDate).HasColumnType("datetime");
            entity.Property(report => report.Url).HasMaxLength(2048).IsUnicode(false).IsRequired();
        });

        modelBuilder.Entity<ImportTarget>(entity =>
        {
            entity.ToTable("ImportTarget");

            entity.HasIndex(e => e.Md5, "IX_ImportTarget_MD5").IsUnique();

            entity.Property(e => e.ImportDate).HasColumnType("datetime");
            entity.Property(e => e.Md5)
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnName("MD5");
            entity.Property(e => e.Nip)
                .HasMaxLength(32)
                .IsUnicode(false)
                .HasColumnName("NIP");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
