using System.Collections.Concurrent;
using System.Diagnostics;
using ClimbOn.Api.Jobs;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Postcodes;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClimbOn.Integration.Tests.Harness;

// Routes and jobs that exist only in the test host. ApiFactory adds them; nothing under src/ does,
// so the production image and its OpenAPI document never contain them.
public sealed class TestEndpoints : IStartupFilter
{
    public const string Prefix = "/_test";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet($"{Prefix}/clock", (IClock clock) => Results.Ok(new ClockProbe(clock.UtcNow)));

            // An ordinary endpoint that needs the database, for the 503 database_unavailable path.
            endpoints.MapGet($"{Prefix}/database", async (ClimbOnDbContext db, CancellationToken cancellationToken) =>
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
                return Results.NoContent();
            });

            // C33 probes: each puts a value where telemetry would carry it.
            endpoints.MapPost($"{Prefix}/telemetry/log", (ProbeValue probe, ILogger<TestEndpoints> logger) =>
            {
                logger.LogInformation("Telemetry probe {Value}", probe.Value);
                return Results.NoContent();
            });
            endpoints.MapPost($"{Prefix}/telemetry/tag", (ProbeValue probe) =>
            {
                Activity.Current?.SetTag("probe.value", probe.Value);
                return Results.NoContent();
            });
            // Tags its span only after the client has the response, while the span is still open.
            endpoints.MapPost($"{Prefix}/telemetry/late-tag", (ProbeValue probe, HttpContext context) =>
            {
                var span = Activity.Current;
                context.Response.OnCompleted(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300));
                    span?.SetTag("probe.value", probe.Value);
                });
                return Results.NoContent();
            });
            endpoints.MapGet($"{Prefix}/telemetry/query/{{value}}", () => Results.NoContent());

            // Postcodes (S15, S16) until the profile endpoints (task 16) use them.
            endpoints.MapPost($"{Prefix}/postcodes/validate", async (ProbeValue probe, PostcodeValidator validator, CancellationToken cancellationToken) =>
            {
                await validator.ValidateAsync(probe.Value, cancellationToken);
                return Results.NoContent();
            });
            endpoints.MapPost($"{Prefix}/postcodes/point", async (ProbeValue probe, PostcodeLookup lookup, CancellationToken cancellationToken) =>
                await lookup.FindPointAsync(probe.Value, cancellationToken) is { } point
                    ? Results.Ok(new PointProbe(point.Latitude, point.Longitude))
                    : Results.NotFound());
        });
        next(app);
    };
}

public sealed record ClockProbe(DateTimeOffset UtcNow);

public sealed record ProbeValue(string Value);

public sealed record PointProbe(double Latitude, double Longitude);

// Records each run so the harness can prove RunJobAsync dispatches through JobRunner on the test host.
public sealed class ProbeJob(IClock clock, ProbeJob.Runs runs) : IJob
{
    public const string JobName = "_test-probe";

    public string Name => JobName;

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        runs.Record(clock.UtcNow);
        return Task.FromResult(0);
    }

    public sealed class Runs
    {
        private readonly ConcurrentQueue<DateTimeOffset> times = new();

        public IReadOnlyList<DateTimeOffset> Times => times.ToArray();

        public void Record(DateTimeOffset time) => times.Enqueue(time);
    }
}
