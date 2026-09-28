using ColdCall.Data;
using ColdCall.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience = builder.Configuration["Google:ClientId"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
            ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Google:ClientId"])
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
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.UseHttpsRedirection();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.UseSpa(spa =>
{
    spa.Options.SourcePath = "ClientApp";
    if (app.Environment.IsDevelopment())
    {
        spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
    }
});

app.Run();
