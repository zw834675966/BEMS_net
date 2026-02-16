using Npgsql;

namespace Bems.Web.Infrastructure;

public sealed class PostgresProbeService
{
    private readonly string? _connectionString;
    private readonly ILogger<PostgresProbeService> _logger;

    public PostgresProbeService(IConfiguration configuration, ILogger<PostgresProbeService> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres");
        _logger = logger;
    }

    public async Task<DbHealthResult> PingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return DbHealthResult.Fail("ConnectionStrings:Postgres is empty.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            var ok = result is 1 or long and 1L;

            return ok
                ? DbHealthResult.Ok()
                : DbHealthResult.Fail("Database probe returned an unexpected result.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL probe failed.");
            return DbHealthResult.Fail(ex.Message);
        }
    }

    public async Task<TelemetryDemoRow?> GetLatestTelemetryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT id, value, created_at
            FROM bems.telemetry_demo
            ORDER BY id DESC
            LIMIT 1;
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TelemetryDemoRow(
            reader.GetInt64(0),
            reader.GetDouble(1),
            reader.GetDateTime(2));
    }

    public async Task<TelemetryDemoRow> InsertTelemetryAsync(double value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is empty.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var bootstrap = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS bems;

            CREATE TABLE IF NOT EXISTS bems.telemetry_demo (
                id BIGSERIAL PRIMARY KEY,
                value DOUBLE PRECISION NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """,
            connection);
        await bootstrap.ExecuteNonQueryAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO bems.telemetry_demo (value)
            VALUES (@value)
            RETURNING id, value, created_at;
            """,
            connection);
        command.Parameters.AddWithValue("value", value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new TelemetryDemoRow(
            reader.GetInt64(0),
            reader.GetDouble(1),
            reader.GetDateTime(2));
    }
}

public sealed record DbHealthResult(bool IsSuccess, string Message, DateTimeOffset CheckedAtUtc)
{
    public static DbHealthResult Ok() => new(true, "ok", DateTimeOffset.UtcNow);
    public static DbHealthResult Fail(string message) => new(false, message, DateTimeOffset.UtcNow);
}

public sealed record TelemetryDemoRow(long Id, double Value, DateTime CreatedAt);
