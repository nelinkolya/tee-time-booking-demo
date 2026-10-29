using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TeeTimeBooking.Api;
using TeeTimeBooking.Api.Bookings;
using TeeTimeBooking.Api.Data;
using TeeTimeBooking.Api.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BookingDb>(o => o
    .UseSqlServer(
        builder.Configuration.GetConnectionString("TeeTimes")
            ?? throw new InvalidOperationException("Set ConnectionStrings:TeeTimes (dotnet user-secrets)."),
        // A paused serverless database takes up to a minute to resume.
        sql => sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null))
    // Racing duplicates collide on the idempotency index by design; EF would log each one as an error.
    // Unhandled failures are still logged as errors by the exception handler.
    .ConfigureWarnings(w => w.Log(
        (RelationalEventId.CommandError, LogLevel.Debug),
        (CoreEventId.SaveChangesFailed, LogLevel.Debug))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BookingTelemetry>();
builder.Services.AddScoped<BookingService>();

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("TeeTimeBooking.Api", serviceVersion: AppVersion.Display))
    .WithTracing(t => t
        .AddSource(BookingTelemetry.Name)
        .AddAspNetCoreInstrumentation(o => o.Filter = http => http.Request.Path.StartsWithSegments("/api"))
        .AddSqlClientInstrumentation())
    .WithMetrics(m => m
        .AddMeter(BookingTelemetry.Name)
        .AddAspNetCoreInstrumentation()
        .AddMeter("System.Runtime"))
    .WithLogging(_ => { }, o =>
    {
        o.IncludeScopes = true;
        o.IncludeFormattedMessage = true;
    });
var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsights))
{
    otel.WithTracing(t => t.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsights))
        .WithMetrics(m => m.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsights))
        .WithLogging(l => l.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsights));
}
else if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otel.UseOtlpExporter();
}
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<BookingDb>("database");
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Tee Time Booking API";
    doc.Info.Description =
        "Books spots on golf tee times without overbooking under concurrent requests, and without duplicates " +
        "when one request is retried: send a unique `Idempotency-Key` header per booking attempt and reuse it on retries.";
    // No absolute server URL, so "Try it out" also works through the Vite proxy.
    doc.Servers?.Clear();
    return Task.CompletedTask;
}));

var app = builder.Build();

// Tests drop and migrate their own database.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BookingDb>();
    await db.Database.MigrateAsync();
    await Seed.EnsureTeeTimesAsync(db, DateOnly.FromDateTime(DateTime.Now));
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapBookingEndpoints();
// Readiness (and the general health check): the database must answer.
app.MapHealthChecks("/health");
// Liveness: the process answers, no dependencies — a database outage must not get containers restarted.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapGet("/api/version", (IHostEnvironment env) =>
        TypedResults.Ok(new VersionDto(AppVersion.Version, AppVersion.Commit, AppVersion.BuiltAtUtc, env.EnvironmentName)))
    .WithTags("Meta")
    .WithSummary("The running version: release, commit and build time");

// The built UI in wwwroot (in the container); a wrong /api URL stays a 404 instead of falling back to it.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.MapFallback("/api/{**path}", () => TypedResults.Problem(title: "No such API endpoint.", statusCode: StatusCodes.Status404NotFound));

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Tee Time Booking API");
        o.DocumentTitle = "Tee Time Booking API";
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/demo/reset", async (BookingDb db) =>
    {
        await Seed.ResetAsync(db);
        return TypedResults.NoContent();
    })
    .WithTags("Demo")
    .WithSummary("Delete all bookings and free every tee time (Development only)");
}

app.Run();

public partial class Program;
