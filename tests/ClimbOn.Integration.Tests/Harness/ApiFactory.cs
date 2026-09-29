using System.Collections.Concurrent;
using ClimbOn.Api.Jobs;
using ClimbOn.Application.Abstractions;
using ClimbOn.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ClimbOn.Integration.Tests.Harness;

// The api host on real PostgreSQL (C11) with a fixed clock (C4) and recording fakes (C22).
// Default: one container per test collection. With CLIMBON_TEST_DB set: that database, shared by
// every collection, so tests on it run one at a time (C30, task 32).
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Environment = "Test";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SharedDatabaseGates = new();

    private readonly TestDatabaseSettings settings;
    private PostgreSqlContainer? container;
    private string? connectionString;

    public ApiFactory()
        : this(TestDatabaseSettings.FromEnvironment())
    {
    }

    private ApiFactory(TestDatabaseSettings settings)
    {
        this.settings = settings;
        Email = new RecordingEmailSender(Fixtures);
        Data = new TestData(Fixtures);
    }

    // A factory a test owns and initialises itself; such a test carries Compat=exclude.
    public static ApiFactory For(TestDatabaseSettings settings) => new(settings);

    public FixedClock Clock { get; } = new();

    public FixtureValues Fixtures { get; } = new();

    public RecordingEmailSender Email { get; }

    public TestData Data { get; }

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
        }
    }

    // Called at the start of every test: waits for the shared database when there is one,
    // then resets data, clock and recorded email. Dispose the lease when the test ends.
    public async Task<IDisposable> BeginTestAsync(CancellationToken cancellationToken)
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
            return new Lease(gate);
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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting($"ConnectionStrings:{ClimbOnDbContext.ConnectionStringName}", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<IStartupFilter, TestEndpoints>();
            services.AddSingleton<ProbeJob.Runs>();
            services.AddScoped<IJob, ProbeJob>();
        });
    }

    private sealed class Lease(SemaphoreSlim? gate) : IDisposable
    {
        public void Dispose() => gate?.Release();
    }
}
