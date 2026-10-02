using System.Collections.Concurrent;
using ClimbOn.Api.Jobs;
using ClimbOn.Application.Abstractions;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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
        });
        next(app);
    };
}

public sealed record ClockProbe(DateTimeOffset UtcNow);

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
