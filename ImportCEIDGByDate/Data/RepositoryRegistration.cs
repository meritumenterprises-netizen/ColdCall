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
                ?? "Server=localhost;Database=Firmy_temp;Integrated Security=True;TrustServerCertificate=True;",
            sql => sql.UseNetTopologySuite()));
        services.AddDbContext<FirmyImportDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("FirmyImport")
                ?? "Server=localhost;Database=Firmy_import;Integrated Security=True;TrustServerCertificate=True;"));
        services.AddScoped<FirmyTempRepository>();
        services.AddScoped<FirmyImportRepository>();
        services.AddScoped<CeidgDatabaseUtils>();
        return services;
    }
}
