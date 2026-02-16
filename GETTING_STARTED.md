# BEMS MVP Quickstart

## 1. Start PostgreSQL + TimescaleDB

```powershell
docker compose up -d postgres
```

## 2. Run Web App

```powershell
$env:DOTNET_CLI_HOME="D:\c#\BEMS\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project .\src\Bems.Web\Bems.Web.csproj
```

## 3. Verify Web -> API -> DB loop

```powershell
curl http://localhost:5000/api/health/live
curl http://localhost:5000/api/health/ready
curl -X POST http://localhost:5000/api/telemetry/demo -H "Content-Type: application/json" -d "{\"value\":123.45}"
curl http://localhost:5000/api/telemetry/demo/latest
```

## Notes

- If ASP.NET Core picks a different port, check console output and replace `5000`.
- `ConnectionStrings:Postgres` is in `src/Bems.Web/appsettings.Development.json`.
