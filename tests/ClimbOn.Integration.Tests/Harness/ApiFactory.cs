using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ClimbOn.Api.Email;
using ClimbOn.Api.Jobs;
using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Postcodes;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Infrastructure.Postcodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Testcontainers.PostgreSql;
using Xunit;

namespace ClimbOn.Integration.Tests.Harness;

// The api host on real PostgreSQL (C11) with a fixed clock (C4), recording fakes (C22) and the
// C33 telemetry scan at the end of every test. Migrating also seeds the fixture postcodes (C44).
// Default: one container per test collection. With CLIMBON_TEST_DB set: that database, shared by
// every collection, so tests on it run one at a time (C30, task 32).
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Environment = "Test";

    // The base of every link in an email.
    public const string PublicBaseUrl = "https://climbon.test";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SharedDatabaseGates = new();

    private readonly TestDatabaseSettings settings;
    private readonly IReadOnlyDictionary<string, string?> hostSettings;
    private PostgreSqlContainer? container;
    private string? connectionString;

    public ApiFactory()
        : this(TestDatabaseSettings.FromEnvironment(), new Dictionary<string, string?>())
    {
    }

    private ApiFactory(TestDatabaseSettings settings, IReadOnlyDictionary<string, string?> hostSettings)
    {
        this.settings = settings;
        this.hostSettings = hostSettings;
        Email = new RecordingEmailSender(Fixtures);
        Data = new TestData(Fixtures);
    }

    // A factory a test owns and initialises itself; such a test carries Compat=exclude.
    // Host settings are added to the api's configuration.
    public static ApiFactory For(TestDatabaseSettings settings, IReadOnlyDictionary<string, string?>? hostSettings = null) =>
        new(settings, hostSettings ?? new Dictionary<string, string?>());

    public FixedClock Clock { get; } = new();

    public FixtureValues Fixtures { get; } = new();

    public RecordingEmailSender Email { get; }

    public KartverketFixture Kartverket { get; } = new();

    public TestData Data { get; }

    public TelemetryCapture Telemetry { get; } = new();

    public bool UsesContainer => container is not null;

    public string ConnectionString =>
        connectionString ?? throw new InvalidOperationException("The factory has not been initialised.");

    public async ValueTask InitializeAsync()
    {
        if (settings.ExternalConnectionString is { } external)
        {
            connectionString = external;
        }
        else
        {
            container = IsolatedPostgres.CreateContainer();
            await container.StartAsync(TestContext.Current.CancellationToken);
            connectionString = container.GetConnectionString();
        }

        if (!settings.SkipMigrations)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Database
                .MigrateAsync(TestContext.Current.CancellationToken);
            await FixturePostcodes.SeedAsync(connectionString, TestContext.Current.CancellationToken);
        }
    }

    // Called at the start of every test: waits for the shared database when there is one,
    // then resets data, clock, recorded email and the served postcode dataset. Dispose the lease when the test ends: it fails
    // the test with TelemetryLeakException if a fixture value reached telemetry (C33).
    public async Task<IAsyncDisposable> BeginTestAsync(CancellationToken cancellationToken)
    {
        SemaphoreSlim? gate = null;
        if (!UsesContainer)
        {
            gate = SharedDatabaseGates.GetOrAdd(ConnectionString, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            await DatabaseReset.ResetAsync(ConnectionString, cancellationToken);
            Clock.Reset();
            Email.Clear();
            Kartverket.Reset();
            return new Lease(this, gate);
        }
        catch
        {
            gate?.Release();
            throw;
        }
    }

    // Jobs run in-process on this host through the same dispatch the container entrypoint uses (design §5).
    public Task<int> RunJobAsync(string name) =>
        JobRunner.RunAsync(Services, name, TestContext.Current.CancellationToken);

    // The final C33 scan: what arrived after the last lease's scan, what the host emits while it
    // stops, and everything from a factory a test owns without taking a lease.
    public override async ValueTask DisposeAsync()
    {
        var captured = new List<TelemetryItem>();
        try
        {
            captured.AddRange(await Telemetry.TakeAsync());
            await base.DisposeAsync();
            captured.AddRange(await Telemetry.TakeAsync());
        }
        finally
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }
        }

        var leaks = TelemetryCapture.FindLeaks(captured, Fixtures);
        if (leaks.Count > 0)
        {
            throw new TelemetryLeakException(leaks);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting($"ConnectionStrings:{ClimbOnDbContext.ConnectionStringName}", ConnectionString);
        builder.UseSetting(EmailContent.PublicBaseUrlKey, PublicBaseUrl);

        // The instrumentation masks query values itself by default; off here, so that SpanRedaction
        // is what the tests prove keeps the query string out (C32).
        builder.UseSetting("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "true");
        foreach (var (key, value) in hostSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<IEmailSender>(Email);
            services.AddHttpClient<IPostcodeSource, KartverketSource>().ConfigurePrimaryHttpMessageHandler(Kartverket.CreateHandler);
            services.AddSingleton<IStartupFilter, TestEndpoints>();
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(Telemetry.SpanProcessor));
            services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddProcessor(Telemetry.LogProcessor));
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(Telemetry.MetricReader));
            services.AddSingleton<IMeterFactory>(_ => new HostMeterFactory());
            services.AddSingleton<ProbeJob.Runs>();
            services.AddScoped<IJob, ProbeJob>();
        });
    }

    // Every meter listener in the process sees every host's meters, and OpenTelemetry merges
    // instruments of the same name and meter tags into one stream, which ends when either host stops.
    // A tag per host keeps this host's metrics collected after another host is disposed.
    private sealed class HostMeterFactory : IMeterFactory
    {
        private readonly string host = Guid.NewGuid().ToString("N");
        private readonly ConcurrentDictionary<(string Name, string? Version), Meter> meters = new();

        public Meter Create(MeterOptions options) => meters.GetOrAdd((options.Name, options.Version), _ => new Meter(
            new MeterOptions(options.Name)
            {
                Version = options.Version,
                Tags = [.. options.Tags ?? [], new("test.host", host)],
                Scope = this,
            }));

        public void Dispose()
        {
            foreach (var meter in meters.Values)
            {
                meter.Dispose();
            }
        }
    }

    private sealed class Lease(ApiFactory factory, SemaphoreSlim? gate) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                var leaks = await factory.Telemetry.TakeLeaksAsync(factory.Fixtures);
                if (leaks.Count > 0)
                {
                    throw new TelemetryLeakException(leaks);
                }
            }
            finally
            {
                gate?.Release();
            }
        }
    }
}
