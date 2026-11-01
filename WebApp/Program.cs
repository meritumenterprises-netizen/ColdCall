using ColdCall.Data;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Stripe;
using ColdCall.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

var initializeDatabase = args.Contains("--initialize-database", StringComparer.OrdinalIgnoreCase);
var angularDevelopment = args.Contains("--angular-dev-server", StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(args.Where(arg =>
    !arg.Equals("--initialize-database", StringComparison.OrdinalIgnoreCase) &&
    !arg.Equals("--angular-dev-server", StringComparison.OrdinalIgnoreCase)).ToArray());

builder.Services.AddControllers();
builder.Services.AddDbContext<ColdCallDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Firmy")));
builder.Services.AddDbContext<LocalAuthDbContext>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<ColdCallDbContext>().Database.GetDbConnection()));
builder.Services.AddIdentityCore<LocalAccount>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LocalAuthDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddScoped<IAuthEmailSender, AuthEmailSender>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-FindMeNow.Antiforgery";
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = builder.Configuration.GetValue<bool>("LocalAuth:CrossSiteCookies") ? SameSiteMode.None : SameSiteMode.Lax;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("local-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<AdminAccessService>();
builder.Services.AddScoped<CompanyCsvExportService>();
builder.Services.AddScoped<CsvExportCleanupService>();
builder.Services.AddDataProtection();
builder.Services.AddScoped<TrialPlanService>();
builder.Services.AddScoped<IStripeCheckoutGateway, StripeCheckoutGateway>();
builder.Services.AddScoped<StripePaymentService>();
builder.Services.AddHttpClient<TurnstileVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<PkdClassifier>(client => client.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddSpaStaticFiles(configuration => configuration.RootPath = "ClientApp/dist");
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalAngular", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("LocalAuth:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:4200", "https://localhost:4200" })
            .AllowAnyHeader()
            .AllowAnyMethod().AllowCredentials().WithExposedHeaders("Content-Disposition");
    });
});

builder.Services
    .AddAuthentication("ColdCallAuth")
    .AddPolicyScheme("ColdCallAuth", "Google or local session", options => options.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? JwtBearerDefaults.AuthenticationScheme : LocalAuthentication.Scheme)
    .AddCookie(LocalAuthentication.Scheme, options =>
    {
        options.Cookie.Name = "__Host-FindMeNow.Session";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = builder.Configuration.GetValue<bool>("LocalAuth:CrossSiteCookies") ? SameSiteMode.None : SameSiteMode.Lax;
        options.ExpireTimeSpan = LocalAuthentication.SessionLifetime;
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = LocalAuthentication.ValidateCookie;
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    })
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

// Database repair/seeding is an explicit maintenance command, not a startup dependency.
if (initializeDatabase)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ColdCallDbContext>();
    await EnsureDatabaseSchemaAsync(db);
    await DbSeeder.SeedAsync(db);
    app.Logger.LogInformation("Database initialization completed.");
    return;
}

var useAngularCli = app.Environment.IsDevelopment() &&
    (angularDevelopment || app.Configuration.GetValue<bool>("Spa:UseAngularCliServer"));
var developmentServerUrl = app.Environment.IsDevelopment()
    ? app.Configuration["Spa:DevelopmentServerUrl"] : null;
var useBuiltFrontend = !useAngularCli && string.IsNullOrWhiteSpace(developmentServerUrl);
if (useBuiltFrontend &&
    !System.IO.File.Exists(Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist", "index.html")))
    throw new InvalidOperationException(
        "Frontend build missing. Run npm run build in ClientApp once, or use --angular-dev-server in Development.");

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.OnStarting(() =>
    {
        if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
        }
        return Task.CompletedTask;
    });
    await next();
});
app.UseHttpsRedirection();
app.UseStaticFiles();
if (useBuiltFrontend)
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AccountEnabledMiddleware>();
app.UseMiddleware<TermsAcceptanceMiddleware>();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if ((context.User.HasClaim(claim => claim.Type == LocalAuthentication.UserIdClaim)
            || context.Request.Path.StartsWithSegments("/api/account"))
        && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { code = "InvalidCsrfToken", message = "Odswiez sesje i sprobuj ponownie." });
            return;
        }
    }
    await next();
});
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.UseSpa(spa =>
{
    spa.Options.SourcePath = "ClientApp";
    if (useAngularCli)
    {
        spa.UseAngularCliServer(npmScript: "start");
    }
    else if (!string.IsNullOrWhiteSpace(developmentServerUrl))
    {
        spa.UseProxyToSpaDevelopmentServer(developmentServerUrl);
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

        await EnsureBusinessTypeTableAsync(connection);
        await EnsurePkdCodeWithoutDotsColumnAsync(connection);
        await EnsureCompanyCreatedOnColumnAsync(connection);
        await EnsureCompanyStatusAsync(connection);
        await EnsureDictionaryNamesAsync(connection);
        await EnsureSearchProfileCriteriaAsync(connection);
        await ExecuteSchemaResourceAsync(connection, "CompanyUserNotes");
        await ExecuteSchemaResourceAsync(connection, "SearchProfileLastSaved");
        await ExecuteSchemaResourceAsync(connection, "StripePayments");
        await EnsurePlanDefinitionsDescriptionHtmlColumnAsync(connection);
    }
    finally
    {
        await connection.CloseAsync();
    }
}

static async Task EnsureSearchProfileCriteriaAsync(System.Data.Common.DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SET XACT_ABORT ON;
        BEGIN TRY
            BEGIN TRANSACTION;
            IF OBJECT_ID(N'ColdCall.SearchProfileCriteria', N'U') IS NULL
            BEGIN
                CREATE TABLE [ColdCall].[SearchProfileCriteria] (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SearchProfileCriteria] PRIMARY KEY,
                    [SearchProfileId] int NOT NULL,
                    [SortColumn] nvarchar(32) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_SortColumn] DEFAULT N'dataAktywnosci',
                    [SortDirection] varchar(4) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_SortDirection] DEFAULT 'desc',
                    [Address] nvarchar(1000) NOT NULL CONSTRAINT [DF_SearchProfileCriteria_Address] DEFAULT N'',
                    [HasPhone] bit NULL,
                    [HasEmail] bit NULL,
                    [HasWebsite] bit NULL,
                    [DateRangeEnabled] bit NOT NULL CONSTRAINT [DF_SearchProfileCriteria_DateRangeEnabled] DEFAULT 0,
                    [DateFrom] date NULL,
                    [DateTo] date NULL,
                    [RecordLimit] int NOT NULL CONSTRAINT [DF_SearchProfileCriteria_RecordLimit] DEFAULT 1000,
                    CONSTRAINT [FK_SearchProfileCriteria_SearchProfiles_SearchProfileId]
                        FOREIGN KEY ([SearchProfileId]) REFERENCES [ColdCall].[SearchProfiles] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_SearchProfileCriteria_SortColumn] CHECK ([SortColumn] IN (N'name',N'nip',N'fullAddress',N'phone',N'email',N'www',N'dataAktywnosci')),
                    CONSTRAINT [CK_SearchProfileCriteria_SortDirection] CHECK ([SortDirection] IN ('asc','desc')),
                    CONSTRAINT [CK_SearchProfileCriteria_RecordLimit] CHECK ([RecordLimit] BETWEEN 1 AND 10000),
                    CONSTRAINT [CK_SearchProfileCriteria_Dates] CHECK (
                        ([DateRangeEnabled] = 0 AND [DateFrom] IS NULL AND [DateTo] IS NULL) OR
                        ([DateRangeEnabled] = 1 AND [DateFrom] IS NOT NULL AND [DateTo] IS NOT NULL AND [DateTo] > [DateFrom]))
                );
                CREATE UNIQUE INDEX [IX_SearchProfileCriteria_SearchProfileId]
                    ON [ColdCall].[SearchProfileCriteria] ([SearchProfileId]);
            END;
            IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'CompanyName') IS NULL
                ALTER TABLE [ColdCall].[SearchProfileCriteria] ADD [CompanyName] nvarchar(1000) NOT NULL
                    CONSTRAINT [DF_SearchProfileCriteria_CompanyName] DEFAULT N'' WITH VALUES;
            IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'Name') IS NULL
                ALTER TABLE [ColdCall].[SearchProfileCriteria] ADD [Name] nvarchar(200) NOT NULL
                    CONSTRAINT [DF_SearchProfileCriteria_Name] DEFAULT N'' WITH VALUES;
            IF COL_LENGTH(N'ColdCall.SearchProfileCriteria', N'CompanyStatusIds') IS NULL
                ALTER TABLE [ColdCall].[SearchProfileCriteria] ADD [CompanyStatusIds] nvarchar(1000) NULL;
            INSERT INTO [ColdCall].[SearchProfileCriteria] ([SearchProfileId])
            SELECT p.[Id] FROM [ColdCall].[SearchProfiles] p
            WHERE NOT EXISTS (SELECT 1 FROM [ColdCall].[SearchProfileCriteria] c WHERE c.[SearchProfileId] = p.[Id]);
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureBusinessTypeTableAsync(System.Data.Common.DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        IF OBJECT_ID(N'ColdCall.BusinessType', N'U') IS NULL
        BEGIN
            CREATE TABLE [ColdCall].[BusinessType]
            (
                [TypDzialalnosciId] int NOT NULL,
                [TypDzialalnosci] varchar(512) NOT NULL,
                CONSTRAINT [PK_BusinessType] PRIMARY KEY ([TypDzialalnosciId])
            )
        END

        IF NOT EXISTS (SELECT 1 FROM [ColdCall].[BusinessType] WHERE [TypDzialalnosciId] = 1)
        BEGIN
            INSERT INTO [ColdCall].[BusinessType] ([TypDzialalnosciId], [TypDzialalnosci])
            VALUES (1, N'Jednoosobowa działalność gospodarcza')
        END

        INSERT INTO [ColdCall].[BusinessType] ([TypDzialalnosciId], [TypDzialalnosci])
        SELECT DISTINCT c.[BusinessType], CONVERT(varchar(512), CONCAT(N'Typ działalności ', c.[BusinessType]))
        FROM [ColdCall].[Companies] c
        WHERE c.[BusinessType] IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM [ColdCall].[BusinessType] bt
              WHERE bt.[TypDzialalnosciId] = c.[BusinessType]
          )

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.foreign_keys
            WHERE name = N'FK_Companies_BusinessType_BusinessType'
              AND parent_object_id = OBJECT_ID(N'ColdCall.Companies')
        )
        BEGIN
            ALTER TABLE [ColdCall].[Companies] WITH CHECK
            ADD CONSTRAINT [FK_Companies_BusinessType_BusinessType]
            FOREIGN KEY ([BusinessType])
            REFERENCES [ColdCall].[BusinessType] ([TypDzialalnosciId])
        END
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task EnsurePkdCodeWithoutDotsColumnAsync(System.Data.Common.DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_PADDING ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET NUMERIC_ROUNDABORT OFF;

        IF COL_LENGTH(N'ColdCall.PkdCodes', N'CodeWithoutDots') IS NULL
        BEGIN
            ALTER TABLE [ColdCall].[PkdCodes]
            ADD [CodeWithoutDots] AS REPLACE([Code], N'.', N'') PERSISTED;
        END
        """;
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureCompanyStatusAsync(System.Data.Common.DbConnection connection)
{
    await ExecuteSchemaResourceAsync(connection, "CompanyStatus");
}

static async Task EnsureDictionaryNamesAsync(System.Data.Common.DbConnection connection)
{
    await ExecuteSchemaResourceAsync(connection, "DictionaryNames");
}

static async Task ExecuteSchemaResourceAsync(System.Data.Common.DbConnection connection, string resourceName)
{
    using var stream = typeof(ColdCallDbContext).Assembly.GetManifestResourceStream($"ColdCall.Sql.{resourceName}.sql")
        ?? throw new InvalidOperationException($"{resourceName} SQL resource is missing.");
    using var reader = new StreamReader(stream);
    await using var command = connection.CreateCommand();
    command.CommandTimeout = 600;
    command.CommandText = await reader.ReadToEndAsync();
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureCompanyCreatedOnColumnAsync(System.Data.Common.DbConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandTimeout = 600;
    command.CommandText = """
        SET XACT_ABORT ON;
        BEGIN TRY
            BEGIN TRANSACTION;
            IF COL_LENGTH(N'ColdCall.Companies', N'CreatedOn') IS NULL
            BEGIN
                ALTER TABLE [ColdCall].[Companies]
                ADD [CreatedOn] datetime NOT NULL
                    CONSTRAINT [DF_Companies_CreatedOn] DEFAULT (DATEADD(day, -1, GETDATE())) WITH VALUES;

                ALTER TABLE [ColdCall].[Companies] DROP CONSTRAINT [DF_Companies_CreatedOn];
                ALTER TABLE [ColdCall].[Companies]
                ADD CONSTRAINT [DF_Companies_CreatedOn] DEFAULT (GETDATE()) FOR [CreatedOn];
            END;
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;
    await command.ExecuteNonQueryAsync();
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
