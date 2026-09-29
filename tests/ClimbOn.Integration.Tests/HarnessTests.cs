using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClimbOn.Application.Abstractions;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests;

public sealed class HarnessTests(ApiFactory factory) : IntegrationTest(factory)
{
    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Uses_testcontainers_database_by_default()
    {
        await using var own = ApiFactory.For(TestDatabaseSettings.FromEnvironment(_ => null));
        await own.InitializeAsync();

        Assert.True(own.UsesContainer);
        Assert.StartsWith("16.", await ScalarAsync(own.ConnectionString, "SHOW server_version"));
        Assert.Equal(0L, await CountPendingMigrationsAsync(own));
    }

    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Uses_external_database_when_configured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await server.CreateDatabaseAsync("prepared", ct);
        await server.CreateDatabaseAsync("unmigrated", ct);

        await using var external = ApiFactory.For(TestDatabaseSettings.FromEnvironment(Variables(
            (TestDatabaseSettings.DatabaseVariable, server.ConnectionStringFor("prepared")))));
        await external.InitializeAsync();
        await using var skipping = ApiFactory.For(TestDatabaseSettings.FromEnvironment(Variables(
            (TestDatabaseSettings.DatabaseVariable, server.ConnectionStringFor("unmigrated")),
            (TestDatabaseSettings.SkipMigrationsVariable, "1"))));
        await skipping.InitializeAsync();

        Assert.False(external.UsesContainer);
        Assert.Equal("prepared", HostDatabaseName(external));
        Assert.Equal(0L, await CountPendingMigrationsAsync(external));
        Assert.False(skipping.UsesContainer);
        Assert.Equal("unmigrated", HostDatabaseName(skipping));
        Assert.NotEqual(0L, await CountPendingMigrationsAsync(skipping));
    }

    [Fact]
    [Trait("Compat", "exclude")]
    public async Task External_database_run_is_serial_and_isolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await server.CreateDatabaseAsync("shared", ct);
        var settings = TestDatabaseSettings.FromEnvironment(Variables(
            (TestDatabaseSettings.DatabaseVariable, server.ConnectionStringFor("shared"))));
        await using var first = ApiFactory.For(settings);
        await using var second = ApiFactory.For(settings);
        await first.InitializeAsync();
        await second.InitializeAsync();
        await server.ExecuteAsync("shared", "CREATE TABLE harness_probe (owner text NOT NULL)", ct);

        // Two collections overlap in time. Unless the harness serialises and resets, the second
        // one's reset wipes the first one's row, or the second sees both rows.
        var seen = await Task.WhenAll(
            WriteThenReadAsync(first, "first", TimeSpan.Zero),
            WriteThenReadAsync(second, "second", TimeSpan.FromMilliseconds(100)));

        Assert.Equal(["first"], seen[0]);
        Assert.Equal(["second"], seen[1]);
    }

    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Reset_empties_tables_the_model_does_not_know()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(ct);
        await using var own = ApiFactory.For(new TestDatabaseSettings(server.ConnectionString, SkipMigrations: false));
        await own.InitializeAsync();
        var database = new NpgsqlConnectionStringBuilder(server.ConnectionString).Database!;
        await server.ExecuteAsync(database, """
            CREATE TABLE later_parent (id int PRIMARY KEY);
            CREATE TABLE later_child (id serial PRIMARY KEY, parent int NOT NULL REFERENCES later_parent (id));
            INSERT INTO later_parent VALUES (1);
            INSERT INTO later_child (parent) VALUES (1);
            """, ct);

        using (await own.BeginTestAsync(ct))
        {
            Assert.Equal("0", await ScalarAsync(own.ConnectionString, "SELECT count(*) FROM later_parent"));
            Assert.Equal("0", await ScalarAsync(own.ConnectionString, "SELECT count(*) FROM later_child"));
            Assert.NotEqual("0", await ScalarAsync(own.ConnectionString, """SELECT count(*) FROM "__EFMigrationsHistory" """));
        }
    }

    [Fact]
    public void Tests_needing_their_own_database_are_compat_excluded()
    {
        var tests = typeof(HarnessTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<FactAttribute>().Any())
                .Select(method => (Name: $"{type.Name}.{method.Name}", OwnDatabase: UsesOwnDatabase(method),
                    Excluded: IsCompatExcluded(type) || IsCompatExcluded(method))))
            .ToList();

        // The detection itself must see the known cases, or an empty result would prove nothing.
        Assert.Contains(tests, test => test is { Name: "HealthEndpointTests.Pending_migration_returns_503", OwnDatabase: true });
        Assert.Contains(tests, test => test is { Name: "HarnessTests.Uses_testcontainers_database_by_default", OwnDatabase: true });
        Assert.Contains(tests, test => test is { Name: "HealthEndpointTests.Healthy_returns_200_ok", OwnDatabase: false });
        Assert.Empty(tests.Where(test => test.OwnDatabase != test.Excluded)
            .Select(test => $"{test.Name}: own database {test.OwnDatabase}, Compat=exclude {test.Excluded}"));
    }

    [Fact]
    public void Production_host_has_no_test_routes()
    {
        using var production = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        // The test host must list the route, or finding none in production would prove nothing.
        Assert.Contains(RoutePatterns(Factory), pattern => pattern.StartsWith(TestEndpoints.Prefix + "/", StringComparison.Ordinal));
        Assert.DoesNotContain(RoutePatterns(production), pattern => pattern.StartsWith(TestEndpoints.Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Host_reads_the_fixed_clock()
    {
        var now = new DateTimeOffset(2027, 3, 4, 5, 6, 7, TimeSpan.Zero);
        Factory.Clock.Set(now);
        using var client = Factory.CreateClient();

        var probe = await client.GetFromJsonAsync<ClockProbe>($"{TestEndpoints.Prefix}/clock", TestContext.Current.CancellationToken);

        Assert.Equal(now, probe?.UtcNow);
    }

    [Fact]
    public async Task Email_through_the_port_is_recorded_and_its_link_token_registered()
    {
        var sender = Factory.Services.GetRequiredService<IEmailSender>();
        var message = new EmailMessage("someone@example.test", "Bekreft", "Klikk https://climbon.test/confirm#token=abc123XYZ for å bekrefte.");

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal([message], Factory.Email.Sent);
        Assert.True(Factory.Fixtures.Contains("abc123XYZ"));
    }

    [Fact]
    public void Test_data_registers_every_personal_value()
    {
        var account = Factory.Data.Account(new DateOnly(1991, 2, 3));

        Assert.All(
            [account.Email, account.Password, account.DisplayName, "1991-02-03", "03.02.1991"],
            value => Assert.True(Factory.Fixtures.Contains(value), value));
    }

    [Fact]
    public async Task Jobs_run_in_process_through_JobRunner_with_the_fixed_clock()
    {
        var runs = Factory.Services.GetRequiredService<ProbeJob.Runs>();
        var before = runs.Times.Count;
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        var exitCode = await Factory.RunJobAsync(ProbeJob.JobName);

        Assert.Equal(0, exitCode);
        Assert.Equal(FixedClock.Start.AddHours(2), runs.Times[before]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Factory.RunJobAsync("no-such-job"));
    }

    private static async Task<string[]> WriteThenReadAsync(ApiFactory factory, string owner, TimeSpan startAfter)
    {
        var ct = TestContext.Current.CancellationToken;
        await Task.Delay(startAfter, ct);
        using (await factory.BeginTestAsync(ct))
        {
            await using var connection = new NpgsqlConnection(factory.ConnectionString);
            await connection.OpenAsync(ct);
            await using (var insert = new NpgsqlCommand("INSERT INTO harness_probe VALUES (@owner)", connection))
            {
                insert.Parameters.AddWithValue("owner", owner);
                await insert.ExecuteNonQueryAsync(ct);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);

            var owners = new List<string>();
            await using var select = new NpgsqlCommand("SELECT owner FROM harness_probe ORDER BY owner", connection);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                owners.Add(reader.GetString(0));
            }

            return [.. owners];
        }
    }

    // A test owns its database when it builds its own factory or server instead of using the class fixture.
    private static bool UsesOwnDatabase(MethodInfo method)
    {
        var types = (method.GetMethodBody()?.LocalVariables ?? []).Select(local => local.LocalType).ToList();
        if (method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType is { } stateMachine)
        {
            types.AddRange(stateMachine.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType));
            types.AddRange((stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetMethodBody()?.LocalVariables ?? []).Select(local => local.LocalType));
        }

        return types.Any(type => typeof(ApiFactory).IsAssignableFrom(type) || typeof(IsolatedPostgres).IsAssignableFrom(type));
    }

    private static bool IsCompatExcluded(MemberInfo member) =>
        member.GetCustomAttributes<TraitAttribute>().Any(trait => trait is { Name: "Compat", Value: "exclude" });

    private static IReadOnlyList<string> RoutePatterns<T>(WebApplicationFactory<T> host)
        where T : class =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText?.TrimStart('/'))
            .ToList();

    private static Func<string, string?> Variables(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(variable => variable.Name == name).Value;

    private static string HostDatabaseName(ApiFactory host)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Database.GetDbConnection().Database;
    }

    private static async Task<long> CountPendingMigrationsAsync(ApiFactory host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<ClimbOnDbContext>().Database
            .GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        return pending.LongCount();
    }

    private static async Task<string> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
