using System.Globalization;
using Npgsql;

namespace ClimbOn.Integration.Tests.Harness;

public sealed record FixturePostcode(string Code, double Latitude, double Longitude, bool Retired);

// The fixed postcode table every test runs against (C44), read from Fixtures/postcodes.csv.
public static class FixturePostcodes
{
    // In the table, retired.
    public const string Retired = "3089";

    // Four digits, not in the table.
    public const string Unknown = "0001";

    public static IReadOnlyList<FixturePostcode> All { get; } = Load();

    public static FixturePostcode Get(string code) => All.Single(postcode => postcode.Code == code);

    // Idempotent, so every factory on a shared database (CLIMBON_TEST_DB) may run it.
    public static async Task SeedAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO postcodes (code, lat, lon, retired)
            SELECT * FROM unnest(@codes, @lats, @lons, @retired)
            ON CONFLICT (code) DO UPDATE SET lat = EXCLUDED.lat, lon = EXCLUDED.lon, retired = EXCLUDED.retired
            """,
            connection);
        command.Parameters.AddWithValue("codes", All.Select(postcode => postcode.Code).ToArray());
        command.Parameters.AddWithValue("lats", All.Select(postcode => postcode.Latitude).ToArray());
        command.Parameters.AddWithValue("lons", All.Select(postcode => postcode.Longitude).ToArray());
        command.Parameters.AddWithValue("retired", All.Select(postcode => postcode.Retired).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<FixturePostcode> Load() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "postcodes.csv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split(','))
            .Select(fields => new FixturePostcode(
                fields[0],
                double.Parse(fields[1], CultureInfo.InvariantCulture),
                double.Parse(fields[2], CultureInfo.InvariantCulture),
                bool.Parse(fields[3])))
            .ToList();
}
