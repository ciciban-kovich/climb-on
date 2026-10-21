using Azure.Monitor.OpenTelemetry.Exporter;
using ClimbOn.Infrastructure.Email;
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

    private const string HostingDiagnostics = "Microsoft.AspNetCore.Hosting.Diagnostics";

    // A wildcard rule matches when the category starts with the part before '*' and ends with the part
    // after it, so no rule for the category can be longer than this one; of equally long rules the last wins.
    private const string HostingDiagnosticsCap = HostingDiagnostics + "*" + HostingDiagnostics;

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var azureMonitor = builder.Configuration[AzureMonitorConnectionString];
        var exportToAzureMonitor = !string.IsNullOrWhiteSpace(azureMonitor);

        // The hosting request lines carry the raw path and query string. A rule that names a provider
        // beats one that names none, so the cap is repeated for every provider a configured rule names;
        // a stricter configured level stays.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            foreach (var provider in options.Rules.Select(rule => rule.ProviderName).Append(null).Distinct().ToList())
            {
                var configured = HostingDiagnosticsRule(options, provider);
                var level = configured?.LogLevel ?? options.MinLevel;
                options.Rules.Add(new LoggerFilterRule(
                    provider, HostingDiagnosticsCap, level > LogLevel.Warning ? level : LogLevel.Warning, configured?.Filter));
            }
        });
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
                metrics.AddAspNetCoreInstrumentation().AddMeter(EmailMetrics.MeterName);
                if (exportToAzureMonitor)
                {
                    metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = azureMonitor);
                }
            });

        return builder;
    }

    // The rule the logging rule selector picks for the hosting category and this provider: a provider's
    // own rules first, then the longest category, then the last.
    private static LoggerFilterRule? HostingDiagnosticsRule(LoggerFilterOptions options, string? provider) =>
        options.Rules
            .Where(rule => rule.ProviderName == provider || rule.ProviderName is null)
            .Where(rule => rule.CategoryName is null || MatchesHostingDiagnostics(rule.CategoryName))
            .OrderBy(rule => rule.ProviderName is null ? 0 : 1)
            .ThenBy(rule => rule.CategoryName?.Length ?? 0)
            .LastOrDefault();

    private static bool MatchesHostingDiagnostics(string category)
    {
        var wildcard = category.IndexOf('*');
        var prefix = wildcard < 0 ? category : category[..wildcard];
        var suffix = wildcard < 0 ? string.Empty : category[(wildcard + 1)..];
        return HostingDiagnostics.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && HostingDiagnostics.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
