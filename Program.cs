using ColdCall.Data;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<ColdCallDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Firmy")));
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<AdminAccessService>();
builder.Services.AddSpaStaticFiles(configuration => configuration.RootPath = "ClientApp/dist");
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalAngular", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200", "https://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var googleClientId = builder.Configuration["Google:ClientId"];
        options.Authority = "https://accounts.google.com";
        options.MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";
        options.Audience = googleClientId;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = !string.IsNullOrWhiteSpace(googleClientId),
            ValidAudience = googleClientId,
            NameClaimType = "name"
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Nie udalo sie zweryfikowac tokena Google.",
                    error = context.Error,
                    errorDescription = context.ErrorDescription,
                    failure = context.AuthenticateFailure?.Message
                });
            }
        };
    });
builder.Services.AddAuthorization();

StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ColdCallDbContext>();
    await EnsureDatabaseSchemaAsync(db);
    await DbSeeder.SeedAsync(db);
}

app.UseHttpsRedirection();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.Equals("/auth-config.json", StringComparison.OrdinalIgnoreCase))
    {
        await context.Response.WriteAsJsonAsync(new
        {
            googleClientId = app.Configuration["Google:ClientId"] ?? ""
        });
        return;
    }

    await next();
});

app.UseRouting();
app.UseCors("LocalAngular");
app.UseAuthentication();
app.UseAuthorization();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.UseSpa(spa =>
{
    spa.Options.SourcePath = "ClientApp";
    if (app.Environment.IsDevelopment())
    {
        spa.UseAngularCliServer(npmScript: "start");
    }
});

app.Run();

static async Task EnsureDatabaseSchemaAsync(ColdCallDbContext db)
{
    if (await db.Database.EnsureCreatedAsync())
    {
        return;
    }

    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        await using var command = connection.CreateCommand();
        var appTables = new[]
        {
            "AdminEmails",
            "Companies",
            "CompanyPkds",
            "Gminy",
            "Miejscowosci",
            "PkdCodes",
            "PlanDefinitions",
            "Powiaty",
            "SearchProfilePkds",
            "SearchProfiles",
            "UserPlans",
            "Users",
            "Wojewodztwa"
        };

        command.CommandText = $"""
            SELECT COUNT(*)
            FROM sys.tables
            WHERE schema_id = SCHEMA_ID(N'ColdCall')
              AND name IN ({string.Join(", ", appTables.Select((_, index) => $"@p{index}"))})
            """;
        for (var i = 0; i < appTables.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = appTables[i];
            command.Parameters.Add(parameter);
        }

        var existingAppTableCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        if (existingAppTableCount == 0)
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync();
        }
        else if (existingAppTableCount != appTables.Length)
        {
            throw new InvalidOperationException(
                "Baza Firmy_temp zawiera czesc tabel aplikacji ColdCall, ale nie caly schemat. Uzupelnij schemat migracja EF albo usun tylko tabele ColdCall.");
        }

        await EnsurePlanDefinitionsDescriptionHtmlColumnAsync(connection);
    }
    finally
    {
        await connection.CloseAsync();
    }
}

static async Task EnsurePlanDefinitionsDescriptionHtmlColumnAsync(System.Data.Common.DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        IF COL_LENGTH(N'ColdCall.PlanDefinitions', N'DescriptionHtml') IS NULL
        BEGIN
            ALTER TABLE [ColdCall].[PlanDefinitions]
            ADD [DescriptionHtml] nvarchar(max) NOT NULL
                CONSTRAINT [DF_PlanDefinitions_DescriptionHtml] DEFAULT N''
        END
        """;
    await command.ExecuteNonQueryAsync();
}
