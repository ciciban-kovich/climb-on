using Xunit;

namespace ClimbOn.Integration.Tests.Harness;

// Base for tests on the shared ApiFactory: each test starts from an empty database, the start
// time and no recorded email, and holds the shared database alone when CLIMBON_TEST_DB is set.
public abstract class IntegrationTest(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private IDisposable? lease;

    protected ApiFactory Factory => factory;

    public async ValueTask InitializeAsync() =>
        lease = await factory.BeginTestAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync()
    {
        lease?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
