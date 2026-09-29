namespace ClimbOn.Integration.Tests.Harness;

// C30 (task 32): a previous release's tests run against one external, already migrated database.
public sealed record TestDatabaseSettings(string? ExternalConnectionString, bool SkipMigrations)
{
    public const string DatabaseVariable = "CLIMBON_TEST_DB";
    public const string SkipMigrationsVariable = "CLIMBON_TEST_SKIP_MIGRATIONS";

    public static TestDatabaseSettings FromEnvironment() => FromEnvironment(Environment.GetEnvironmentVariable);

    public static TestDatabaseSettings FromEnvironment(Func<string, string?> variable)
    {
        var external = variable(DatabaseVariable);
        return new(string.IsNullOrWhiteSpace(external) ? null : external, variable(SkipMigrationsVariable) == "1");
    }
}
