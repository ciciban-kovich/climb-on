using Xunit;

namespace ClimbOn.Integration.Tests.Harness;

// Base for tests on the shared ApiFactory: each test starts from an empty database, the start
// time and no recorded email, holds the shared database alone when CLIMBON_TEST_DB is set, and
// fails if a fixture value reached telemetry (C33).
public abstract class IntegrationTest(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private IAsyncDisposable? lease;

    protected ApiFactory Factory => factory;

    public async ValueTask InitializeAsync() =>
        lease = await factory.BeginTestAsync(TestContext.Current.CancellationToken);

    // Ends the lease, which runs the C33 telemetry scan; a leak fails this test.
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (lease is not null)
        {
            await lease.DisposeAsync();
        }
    }
}
