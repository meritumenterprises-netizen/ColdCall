using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Data;

public partial class FirmyTempDbContext
{
    public DbSet<CompanyNameGram> CompanyNameGrams => Set<CompanyNameGram>();
    public DbSet<CompanyNameGramBuild> CompanyNameGramBuilds => Set<CompanyNameGramBuild>();
    public DbSet<CompanyNameGramException> CompanyNameGramExceptions => Set<CompanyNameGramException>();
    public DbSet<CompanyNameGramFrequency> CompanyNameGramFrequencies => Set<CompanyNameGramFrequency>();
    public DbSet<CompanyNameGramPosition> CompanyNameGramPositions => Set<CompanyNameGramPosition>();

    private static void ConfigureNameGramSchema(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyNameGram>(entity =>
        {
            entity.ToTable("CompanyNameGrams", "ColdCall");
            entity.HasKey(row => new { row.CompanyId, row.Gram }).HasName("PK_CompanyNameGrams");
            entity.Property(row => row.CompanyId).ValueGeneratedNever();
            entity.Property(row => row.Gram).HasMaxLength(3).UseCollation("SQL_Latin1_General_CP1_CI_AS");
            entity.HasIndex(row => new { row.Gram, row.CompanyId }).IsUnique()
                .HasDatabaseName("IX_CompanyNameGrams_Gram_CompanyId");
        });
        modelBuilder.Entity<CompanyNameGramBuild>(entity =>
        {
            entity.ToTable("CompanyNameGramBuild", "ColdCall");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<CompanyNameGramException>(entity =>
        {
            entity.ToTable("CompanyNameGramExceptions", "ColdCall");
            entity.HasKey(row => row.CompanyId);
            entity.Property(row => row.CompanyId).ValueGeneratedNever();
            entity.Property(row => row.Name).IsRequired().HasMaxLength(1000)
                .UseCollation("SQL_Latin1_General_CP1_CI_AS");
        });
        modelBuilder.Entity<CompanyNameGramFrequency>(entity =>
        {
            entity.ToTable("CompanyNameGramFrequency", "ColdCall");
            entity.HasKey(row => row.Gram);
            entity.Property(row => row.Gram).HasMaxLength(3).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        });
        modelBuilder.Entity<CompanyNameGramPosition>(entity =>
        {
            entity.ToTable("CompanyNameGramPositions", "ColdCall");
            entity.HasKey(row => row.Position);
            entity.Property(row => row.Position).ValueGeneratedNever();
        });
    }
}

