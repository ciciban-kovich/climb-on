using Npgsql;
using Testcontainers.PostgreSql;

namespace ClimbOn.Integration.Tests.Harness;

// A private server for a test that needs state the shared database cannot give.
// Every test using it carries [Trait("Compat", "exclude")]; HarnessTests enforces that.
public sealed class IsolatedPostgres : IAsyncDisposable
{
    // The production server's major version (task 29).
    public const string Image = "postgres:16";

    private readonly PostgreSqlContainer container;

    private IsolatedPostgres(PostgreSqlContainer container, string connectionString)
    {
        this.container = container;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static PostgreSqlContainer CreateContainer() => new PostgreSqlBuilder(Image).Build();

    public static async Task<IsolatedPostgres> StartAsync(CancellationToken cancellationToken)
    {
        var container = CreateContainer();
        await container.StartAsync(cancellationToken);
        return new IsolatedPostgres(container, container.GetConnectionString());
    }

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString;

    public Task CreateDatabaseAsync(string database, CancellationToken cancellationToken) =>
        RunAsync(ConnectionString, $"CREATE DATABASE \"{database}\"", cancellationToken);

    public Task ExecuteAsync(string database, string sql, CancellationToken cancellationToken) =>
        RunAsync(ConnectionStringFor(database), sql, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => container.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => container.DisposeAsync();

    private static async Task RunAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
