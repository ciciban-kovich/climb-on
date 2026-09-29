using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClimbOn.Api.Health;

public static class HealthResponse
{
    // Status codes follow the report (200 healthy, 503 otherwise); the smoke check relies on the code, not the body.
    public static HealthCheckOptions Options { get; } = new()
    {
        ResponseWriter = (context, report) =>
        {
            var database = report.Entries.TryGetValue(DatabaseHealthCheck.Name, out var entry)
                && entry.Data.TryGetValue(DatabaseHealthCheck.StateKey, out var state)
                ? (string)state
                : "error";
            var status = report.Status == HealthStatus.Healthy ? "ok" : "unavailable";
            return context.Response.WriteAsJsonAsync(new { status, database });
        },
    };
}
