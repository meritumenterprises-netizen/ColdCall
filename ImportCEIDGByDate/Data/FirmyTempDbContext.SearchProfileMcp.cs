using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ImportCEIDGByDate.Data;

public partial class FirmyTempDbContext
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Replace<ForeignKeyIndexConvention>(services =>
            new SearchProfileMcpIndexConvention(services.GetRequiredService<ProviderConventionSetBuilderDependencies>()));
    }

    private sealed class SearchProfileMcpIndexConvention(ProviderConventionSetBuilderDependencies dependencies)
        : ForeignKeyIndexConvention(dependencies)
    {
        protected override IConventionIndex? CreateIndex(IReadOnlyList<IConventionProperty> properties,
            bool unique, IConventionEntityTypeBuilder entityTypeBuilder)
        {
            // Match the database: this non-unique FK has no supporting index.
            if (entityTypeBuilder.Metadata.ClrType == typeof(SearchProfileMcp)) return null;
            return base.CreateIndex(properties, unique, entityTypeBuilder);
        }
    }
}
