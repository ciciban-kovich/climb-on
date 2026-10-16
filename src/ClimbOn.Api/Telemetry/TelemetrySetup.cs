using Azure.Monitor.OpenTelemetry.Exporter;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ClimbOn.Api.Telemetry;

// Logs, traces and metrics through OpenTelemetry (C31), redacted before any exporter sees them (C32).
public static class TelemetrySetup
{
    public const string ServiceName = "climbon-api";

    // Set per environment by infra/env; without it nothing leaves the process.
    public const string AzureMonitorConnectionString = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var azureMonitor = builder.Configuration[AzureMonitorConnectionString];
        var exportToAzureMonitor = !string.IsNullOrWhiteSpace(azureMonitor);

        // The hosting request lines carry the raw path and query string.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            if (exportToAzureMonitor)
            {
                logging.AddAzureMonitorLogExporter(options => options.ConnectionString = azureMonitor);
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                // Npgsql records the command text, never parameter values.
                tracing.AddAspNetCoreInstrumentation().AddNpgsql();

                // Before every exporter, including those added after Program runs.
                tracing.AddProcessor(new SpanRedaction());
                if (exportToAzureMonitor)
                {
                    tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = azureMonitor);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                if (exportToAzureMonitor)
                {
                    metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = azureMonitor);
                }
            });

        return builder;
    }
}
