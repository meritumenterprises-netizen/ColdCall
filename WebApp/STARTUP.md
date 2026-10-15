# Startup

Normal startup serves ClientApp/dist directly. It does not run database schema
updates, seeding, npm, or Angular compilation.

Build the frontend once, and again after changing frontend source:

```powershell
cd C:\projects\ColdCall\WebApp\ClientApp
npm run build
```

Start the application normally from Visual Studio, or run:

```powershell
dotnet run --project C:\projects\ColdCall\WebApp
```

For live frontend development, opt into the previous Angular CLI behavior:

```powershell
dotnet run --project C:\projects\ColdCall\WebApp -- --angular-dev-server
```

Alternatively, keep Angular running separately and configure
Spa:DevelopmentServerUrl (environment variable Spa__DevelopmentServerUrl)
to its local URL. Backend restarts then reuse that development server.
Spa:UseAngularCliServer=true also enables automatic Angular startup in Development.

Database setup/legacy schema upgrades and seeding are explicit maintenance:

```powershell
dotnet run --project C:\projects\ColdCall\WebApp -- --initialize-database
```

That command may change database schema/data and exits when complete.
Run it deliberately when provisioning/upgrading, not during every app restart.
Existing database schema must already match the EF model for normal API operation.
This does not remove the first request's EF initialization or database/network latency;
it removes those dependencies from serving the initial HTML and static assets.
