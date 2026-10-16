using ColdCall.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Data;

public class LocalAuthDbContext(DbContextOptions<LocalAuthDbContext> options) : IdentityUserContext<LocalAccount, int>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("ColdCall");
        builder.Entity<LocalAccount>(entity =>
        {
            entity.ToTable("UserCredentials");
            entity.Property(u => u.Id).HasColumnName("UserId").ValueGeneratedNever();
            entity.Property(u => u.TrialOffered).HasDefaultValue(false);
            entity.HasIndex(u => u.NormalizedUserName).HasDatabaseName("UX_UserCredentials_NormalizedUserName");
            entity.HasOne<LocalUserLink>().WithOne().HasForeignKey<LocalAccount>(u => u.Id).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<LocalUserLink>().ToTable("Users", "ColdCall", table => table.ExcludeFromMigrations());
        builder.Entity<LocalUserLink>().HasKey(u => u.Id);
        builder.Entity<IdentityUserClaim<int>>().ToTable("UserCredentialClaims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("UserCredentialLogins");
        builder.Entity<IdentityUserLogin<int>>().Property(u => u.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserLogin<int>>().Property(u => u.ProviderKey).HasMaxLength(128);
        builder.Entity<IdentityUserToken<int>>().ToTable("UserCredentialTokens");
        builder.Entity<IdentityUserToken<int>>().Property(u => u.LoginProvider).HasMaxLength(128);
        builder.Entity<IdentityUserToken<int>>().Property(u => u.Name).HasMaxLength(128);
    }
}

public class LocalUserLink
{
    public int Id { get; set; }
}
