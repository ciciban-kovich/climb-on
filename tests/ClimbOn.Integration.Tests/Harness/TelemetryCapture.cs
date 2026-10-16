using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace ClimbOn.Integration.Tests.Harness;

// One exported log record, span or metric point, flattened to named fields.
public sealed record TelemetryItem(string Signal, string Name, IReadOnlyList<KeyValuePair<string, object?>> Fields)
{
    public object? Field(string key) => Fields.FirstOrDefault(field => field.Key == key).Value;
}

// Where a fixture value was found. The value itself is never printed.
public sealed record TelemetryLeak(string Kind, string Signal, string Name, string Field)
{
    public override string ToString() => $"{Kind} in {Signal} '{Name}', field '{Field}'";
}

public sealed class TelemetryLeakException(IReadOnlyList<TelemetryLeak> leaks)
    : Exception("Fixture values reached telemetry (C33): " + string.Join("; ", leaks))
{
    public IReadOnlyList<TelemetryLeak> Leaks { get; } = leaks;
}

// C33: what the api exports (logs, spans with their tags and events, metrics), captured after the
// C32 redaction, i.e. as the Azure Monitor exporter would receive it. ApiFactory attaches it.
public sealed class TelemetryCapture
{
    private static readonly TimeSpan InFlightWait = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<TelemetryItem> items = new();
    private readonly ConcurrentDictionary<Activity, byte> serverSpansInFlight = new();

    public TelemetryCapture()
    {
        SpanProcessor = new SpanCapture(this);
        LogProcessor = new SimpleLogRecordExportProcessor(new LogCapture(this));
        // Delta: each collection holds only what was measured since the last, so a leak in a
        // metric fails the test that caused it and is gone after the scan.
        MetricReader = new BaseExportingMetricReader(new MetricCapture(this))
        {
            TemporalityPreference = MetricReaderTemporalityPreference.Delta,
        };
    }

    public BaseProcessor<Activity> SpanProcessor { get; }

    public BaseProcessor<LogRecord> LogProcessor { get; }

    public MetricReader MetricReader { get; }

    public async Task<IReadOnlyList<TelemetryLeak>> TakeLeaksAsync(FixtureValues fixtures) =>
        FindLeaks(await TakeAsync(), fixtures);

    // Everything captured so far, leaving the capture empty. Server spans end after the client
    // has its response, so this first waits for those running now to end, then collects metrics.
    public async Task<IReadOnlyList<TelemetryItem>> TakeAsync()
    {
        var running = serverSpansInFlight.Keys.ToArray();
        var deadline = Stopwatch.StartNew();
        while (running.Any(serverSpansInFlight.ContainsKey) && deadline.Elapsed < InFlightWait)
        {
            await Task.Delay(10);
        }

        MetricReader.Collect();
        var taken = new List<TelemetryItem>();
        while (items.TryDequeue(out var item))
        {
            taken.Add(item);
        }

        return taken;
    }

    public static IReadOnlyList<TelemetryLeak> FindLeaks(IEnumerable<TelemetryItem> captured, FixtureValues fixtures)
    {
        var entries = fixtures.Entries;
        return captured
            .SelectMany(item => item.Fields.SelectMany(field => entries
                .Where(entry => Matches(entry.Value, entry.Key, field.Value))
                .Select(entry => new TelemetryLeak(entry.Value, item.Signal, item.Name, field.Key))))
            .Distinct()
            .ToList();
    }

    private static bool Matches(string kind, string value, object? field)
    {
        if (kind == FixtureValues.PostcodeKind)
        {
            return Texts(field, numbers: false).Any(text => Regex.IsMatch(
                text, $@"(?<![0-9A-Za-z])(?<![0-9]\.){Regex.Escape(value)}(?![0-9A-Za-z])(?!\.[0-9])", RegexOptions.CultureInvariant));
        }

        return Texts(field, numbers: true).Any(text => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Texts(object? field, bool numbers) => field switch
    {
        null => [],
        string text => [text],
        IEnumerable many => many.Cast<object?>().SelectMany(element => Texts(element, numbers)),
        IFormattable when !numbers => [],
        _ => [Convert.ToString(field, CultureInfo.InvariantCulture) ?? string.Empty],
    };

    private void Add(string signal, string name, List<KeyValuePair<string, object?>> fields) =>
        items.Enqueue(new TelemetryItem(signal, name, fields));

    private sealed class SpanCapture(TelemetryCapture capture) : BaseProcessor<Activity>
    {
        public override void OnStart(Activity data)
        {
            if (data.Kind == ActivityKind.Server)
            {
                capture.serverSpansInFlight.TryAdd(data, 0);
            }
        }

        public override void OnEnd(Activity data)
        {
            var fields = new List<KeyValuePair<string, object?>>
            {
                new("displayName", data.DisplayName),
                new("operationName", data.OperationName),
                new("status", data.StatusDescription),
            };
            fields.AddRange(data.TagObjects.Select(tag => Named("tag", tag.Key, tag.Value)));
            fields.AddRange(data.Baggage.Select(item => Named("baggage", item.Key, item.Value)));
            foreach (var spanEvent in data.Events)
            {
                fields.Add(new("event", spanEvent.Name));
                fields.AddRange(spanEvent.Tags.Select(tag => Named($"event:{spanEvent.Name}", tag.Key, tag.Value)));
            }

            foreach (var link in data.Links)
            {
                fields.AddRange((link.Tags ?? []).Select(tag => Named("link", tag.Key, tag.Value)));
            }

            capture.Add("span", data.Source.Name, fields);
            capture.serverSpansInFlight.TryRemove(data, out _);
        }
    }

    private sealed class LogCapture(TelemetryCapture capture) : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                var fields = new List<KeyValuePair<string, object?>>
                {
                    new("message", record.FormattedMessage),
                    new("body", record.Body),
                    new("eventName", record.EventId.Name),
                    new("exception", record.Exception?.ToString()),
                };
                fields.AddRange((record.Attributes ?? []).Select(attribute => Named("attribute", attribute.Key, attribute.Value)));
                record.ForEachScope(
                    (scope, scopeFields) =>
                    {
                        foreach (var item in scope)
                        {
                            scopeFields.Add(Named("scope", item.Key, item.Value));
                        }
                    },
                    fields);
                capture.Add("log", record.CategoryName ?? string.Empty, fields);
            }

            return ExportResult.Success;
        }
    }

    private sealed class MetricCapture(TelemetryCapture capture) : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var fields = new List<KeyValuePair<string, object?>>
                    {
                        new("description", metric.Description),
                        new("unit", metric.Unit),
                        new("value", Value(metric.MetricType, point)),
                    };
                    foreach (var tag in point.Tags)
                    {
                        fields.Add(Named("tag", tag.Key, tag.Value));
                    }

                    capture.Add("metric", metric.Name, fields);
                }
            }

            return ExportResult.Success;
        }

        private static double Value(MetricType type, in MetricPoint point) => type switch
        {
            _ when type.IsHistogram() => point.GetHistogramSum(),
            _ when type.IsSum() && type.IsDouble() => point.GetSumDouble(),
            _ when type.IsSum() => point.GetSumLong(),
            _ when type.IsGauge() && type.IsDouble() => point.GetGaugeLastValueDouble(),
            _ when type.IsGauge() => point.GetGaugeLastValueLong(),
            _ => double.NaN,
        };
    }

    private static KeyValuePair<string, object?> Named(string prefix, string key, object? value) => new($"{prefix}:{key}", value);
}
