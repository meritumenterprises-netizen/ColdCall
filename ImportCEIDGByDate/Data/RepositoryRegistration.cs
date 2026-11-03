using ImportCEIDGByDate.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ImportCEIDGByDate.Data;

public static class RepositoryRegistration
{
    public static IServiceCollection AddImportRepositories(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FirmyTempDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("FirmyTemp")
                ?? "Server=localhost\\SQLEXPRESS;Database=Firmy_temp;User ID=sa;Password=REDACTED_SECRET;TrustServerCertificate=True;",
            sql => sql.UseNetTopologySuite()));
        services.AddDbContext<FirmyImportDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("FirmyImport")
                ?? "Server=localhost\\SQLEXPRESS;Database=Firmy_import;User ID=sa;Password=REDACTED_SECRET;TrustServerCertificate=True;"));
        services.AddScoped<FirmyTempRepository>();
        services.AddScoped<FirmyImportRepository>();
        services.AddScoped<CeidgDatabaseUtils>();
        return services;
    }
}
