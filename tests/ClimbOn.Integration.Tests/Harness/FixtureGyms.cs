using System.Globalization;
using Npgsql;

namespace ClimbOn.Integration.Tests.Harness;

public sealed record FixtureGym(Guid Id, string Name, string Address, string Postcode, bool Active);

// The fixed gym set every test runs against, read from Fixtures/gyms.csv. No migration seeds gyms:
// only the admin creates them (S17, M2), so until then they exist only here.
public static class FixtureGyms
{
    public static IReadOnlyList<FixtureGym> All { get; } = Load();

    public static IReadOnlyList<FixtureGym> Active { get; } = All.Where(gym => gym.Active).ToList();

    public static FixtureGym Inactive { get; } = All.Single(gym => !gym.Active);

    // Idempotent, so every factory on a shared database (CLIMBON_TEST_DB) may run it.
    public static async Task SeedAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO gyms (id, name, address, postcode, active)
            SELECT * FROM unnest(@ids, @names, @addresses, @postcodes, @active)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name, address = EXCLUDED.address, postcode = EXCLUDED.postcode, active = EXCLUDED.active
            """,
            connection);
        command.Parameters.AddWithValue("ids", All.Select(gym => gym.Id).ToArray());
        command.Parameters.AddWithValue("names", All.Select(gym => gym.Name).ToArray());
        command.Parameters.AddWithValue("addresses", All.Select(gym => gym.Address).ToArray());
        command.Parameters.AddWithValue("postcodes", All.Select(gym => gym.Postcode).ToArray());
        command.Parameters.AddWithValue("active", All.Select(gym => gym.Active).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<FixtureGym> Load() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gyms.csv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(','))
            .Select(fields => new FixtureGym(
                Guid.Parse(fields[0], CultureInfo.InvariantCulture),
                fields[1],
                fields[2],
                fields[3],
                bool.Parse(fields[4])))
            .ToList();
}
