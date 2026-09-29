using Npgsql;

namespace ClimbOn.Integration.Tests.Harness;

// Tables are discovered from the database, never from the EF model: a previous release's
// tests must still empty every table of a newer schema (C30).
public static class DatabaseReset
{
    // Reference data loaded by migrations or fixtures and kept across tests.
    public static readonly IReadOnlyList<string> ReferenceTables = [];

    private const string MigrationsHistory = "__EFMigrationsHistory";

    public static async Task ResetAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = new List<string>();
        await using (var query = new NpgsqlCommand(
            "SELECT tablename FROM pg_tables WHERE schemaname = current_schema() AND tablename <> ALL(@kept)",
            connection))
        {
            query.Parameters.AddWithValue("kept", ReferenceTables.Append(MigrationsHistory).ToArray());
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        if (tables.Count == 0)
        {
            return;
        }

        var list = string.Join(", ", tables.Select(table => "\"" + table.Replace("\"", "\"\"") + "\""));
        await using var truncate = new NpgsqlCommand($"TRUNCATE TABLE {list} RESTART IDENTITY CASCADE", connection);
        await truncate.ExecuteNonQueryAsync(cancellationToken);
    }
}
