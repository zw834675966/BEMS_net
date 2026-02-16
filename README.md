# BEMS_net

Building Energy Management System (BEMS) based on .NET 10.

## Project Structure

- `src/Bems.Web`: Blazor Web App + minimal API endpoints
- `src/Bems.Gateway.Worker`: device gateway worker service
- `src/Bems.Application`: application layer/domain orchestration
- `tests/Bems.Application.Tests`: xUnit tests
- `infra/db/init`: PostgreSQL/TimescaleDB init SQL

## Prerequisites

- .NET SDK 10.0.x
- PostgreSQL 16+ (TimescaleDB optional)

## Quick Start (Local)

1. Configure database connection (PowerShell):

```powershell
$env:ConnectionStrings__Postgres="Host=<host>;Port=5432;Database=bems;Username=<user>;Password=<password>"
```

Initialize DB schema/extension:

```powershell
.\infra\db\run-init.ps1 -Host <host> -Database bems -Username <user> -Password <password>
```

2. Run web app:

```powershell
$env:DOTNET_CLI_HOME="D:\c#\BEMS\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project .\src\Bems.Web\Bems.Web.csproj
```

3. Verify minimum loop (`Web -> API -> DB`):

```powershell
curl http://localhost:5000/api/health/live
curl http://localhost:5000/api/health/ready
curl -X POST http://localhost:5000/api/telemetry/demo -H "Content-Type: application/json" -d "{\"value\":123.45}"
curl http://localhost:5000/api/telemetry/demo/latest
```

If your app listens on a different port, use the port shown in console output.

## CI/CD

- CI workflow: `.github/workflows/ci.yml`
  - restore
  - build
  - test
  - format check
- Publish workflow: `.github/workflows/publish.yml`
  - builds web publish artifact on `main`
- Production deployment workflow: `.github/workflows/deploy-production.yml`
  - deploys `main` to Azure App Service (`production` environment protected)

## Staging Deployment (App Service)

Workflow: `.github/workflows/deploy-staging.yml`

Required repository variables/secrets:

- `vars.AZURE_WEBAPP_NAME_STAGING`: App Service name
- `secrets.AZURE_WEBAPP_PUBLISH_PROFILE_STAGING`: publish profile XML

## Production Deployment (App Service)

Workflow: `.github/workflows/deploy-production.yml`

Required repository variables/secrets:

- `vars.AZURE_WEBAPP_NAME_PRODUCTION`: App Service name
- `secrets.AZURE_WEBAPP_PUBLISH_PROFILE_PRODUCTION`: publish profile XML

## Notes

- `global.json` locks SDK version for reproducible builds.
- `.editorconfig` enforces code style baseline.
- Full Chinese architecture and coding standards docs are kept at repository root.
