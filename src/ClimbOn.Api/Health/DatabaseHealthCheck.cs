using System.Data.Common;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClimbOn.Api.Health;

// Healthy only when the database answers and no migration is pending, so a deploy whose
// migrate step did not run fails the smoke check.
public sealed class DatabaseHealthCheck(ClimbOnDbContext db) : IHealthCheck
{
    public const string Name = "database";
    public const string StateKey = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return Unhealthy("unreachable");
            }

            var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any() ? Unhealthy("migration_pending") : State(HealthStatus.Healthy, "ok");
        }
        catch (Exception exception) when (exception is DbException or TimeoutException or InvalidOperationException)
        {
            return Unhealthy("unreachable");
        }
    }

    private static HealthCheckResult Unhealthy(string state) => State(HealthStatus.Unhealthy, state);

    private static HealthCheckResult State(HealthStatus status, string state) =>
        new(status, data: new Dictionary<string, object> { [StateKey] = state });
}
