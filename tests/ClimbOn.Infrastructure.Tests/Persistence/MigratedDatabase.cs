using ClimbOn.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ClimbOn.Infrastructure.Tests.Persistence;

// A migrated database on real PostgreSQL (C11), one container per test class.
public sealed class MigratedDatabase : IAsyncLifetime
{
    // The production server's major version (task 29).
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ClimbOnDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ClimbOnDbContext>().UseNpgsql(container.GetConnectionString()).Options);

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
