using Bems.Web.Components;
using Bems.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<PostgresProbeService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/health/live", () =>
    Results.Ok(new { status = "live", checkedAtUtc = DateTimeOffset.UtcNow }));

app.MapGet("/api/health/ready", async (PostgresProbeService probe, CancellationToken cancellationToken) =>
{
    var result = await probe.PingAsync(cancellationToken);
    return result.IsSuccess
        ? Results.Ok(result)
        : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPost("/api/telemetry/demo", async (TelemetryWriteRequest request, PostgresProbeService probe, CancellationToken cancellationToken) =>
{
    var row = await probe.InsertTelemetryAsync(request.Value, cancellationToken);
    return Results.Ok(row);
});

app.MapGet("/api/telemetry/demo/latest", async (PostgresProbeService probe, CancellationToken cancellationToken) =>
{
    var row = await probe.GetLatestTelemetryAsync(cancellationToken);
    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.Run();

public sealed record TelemetryWriteRequest(double Value);
