using Microsoft.EntityFrameworkCore;

namespace ColdCall.Data;

// Separate model: initializing OAuth must never recreate the application tables.
public sealed class McpOAuthDbContext(DbContextOptions<McpOAuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("McpOAuth");
    }
}
