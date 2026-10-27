using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Email;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Abstractions;
using ClimbOn.Domain.Accounts;
using ClimbOn.Infrastructure.Persistence;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Accounts;

// S84 until erasure (M2) and S87 for password accounts; the SSO variants of S87 are M2.
public sealed class EmailCollisionTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string AccountsRoute = "/api/climber/accounts";
    private const string LoginRoute = "/api/climber/login";

    // The migration before the one that adds the normal form, for the backfill tests.
    private const string MigrationBeforeNormalForm = "20261021112604_Sessions";

    [Fact]
    [Trait("SpecPartial", "S87")]
    public async Task Taken_address_response_is_identical()
    {
        var owner = Factory.Data.Account();
        await RegisterAsync(owner, "en");
        var newcomer = Factory.Data.Account();

        using var created = await PostRegistrationAsync(Registration(newcomer, "nb"));
        using var taken = await PostRegistrationAsync(Registration(Factory.Data.Account(), "nb") with { Email = owner.Email });

        Assert.Equal(created.StatusCode, taken.StatusCode);
        Assert.Equal(
            await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await taken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Names(created), Names(taken));
        Assert.Equal(created.Headers.Location, taken.Headers.Location);
        Assert.Equal(created.Content.Headers.ContentType, taken.Content.Headers.ContentType);
    }

    [Fact]
    [Trait("SpecPartial", "S84")]
    [Trait("SpecPartial", "S87")]
    public async Task Taken_address_creates_no_account()
    {
        var owner = Factory.Data.Account();
        await RegisterAsync(owner);
        var intruder = Factory.Data.Account();

        await RegisterAsync(intruder with { Email = owner.Email });

        // A second account at the address would make the owner's login fail too: login expects one.
        using var intruderLogin = await PostLoginAsync(owner.Email, intruder.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, intruderLogin.StatusCode);
        using var ownerLogin = await PostLoginAsync(owner.Email, owner.Password);
        Assert.Equal(HttpStatusCode.NoContent, ownerLogin.StatusCode);
    }

    // S105: an email another person's action triggers is in the owner's language, not the requester's.
    [Theory]
    [Trait("SpecPartial", "S87")]
    [Trait("SpecPartial", "S105")]
    [InlineData("en", "nb")]
    [InlineData("nb", "en")]
    public async Task Taken_address_gets_a_notice_with_a_login_link_in_the_owner_language(string ownerLanguage, string requestLanguage)
    {
        var owner = Factory.Data.Account();
        await RegisterAsync(owner, ownerLanguage);
        Factory.Email.Clear();

        await RegisterAsync(Factory.Data.Account() with { Email = owner.Email }, requestLanguage);

        var notice = Assert.Single(Factory.Email.Sent);
        var language = Languages.FromClientSetting(ownerLanguage);
        Assert.Equal(owner.Email, notice.To);
        Assert.Equal(Texts.Get(EmailContent.RegistrationAttemptSubjectKey, language), notice.Subject);
        Assert.Equal(
            string.Format(
                CultureInfo.GetCultureInfo(language.Tag()),
                Texts.Get(EmailContent.RegistrationAttemptBodyKey, language),
                $"{ApiFactory.PublicBaseUrl}/login"),
            notice.Body);
    }

    // The notice goes to the address as the owner registered it, however the newcomer spelled it.
    [Fact]
    [Trait("SpecPartial", "S87")]
    public async Task Notice_goes_to_the_registered_spelling()
    {
        var owner = Factory.Data.Account();
        await RegisterAsync(owner);
        Factory.Email.Clear();

        await RegisterAsync(Factory.Data.Account() with { Email = Factory.Data.Email($" {owner.Email.ToUpperInvariant()} ") });

        Assert.Equal(owner.Email, Assert.Single(Factory.Email.Sent).To);
    }

    // The barrier holds every account insert until all the registrations have reached theirs, so none
    // of them can see another's account before inserting its own: only the unique index can decide.
    [Fact]
    [Trait("SpecPartial", "S84")]
    [Trait("SpecPartial", "S87")]
    public async Task Parallel_registrations_create_one_account()
    {
        const int Registrations = 8;
        var email = Factory.Data.Account().Email;
        var drafts = Enumerable.Range(0, Registrations).Select(_ => Factory.Data.Account() with { Email = email }).ToArray();

        HttpResponseMessage[] responses;
        await using (var barrier = await HoldAccountInsertsAsync(Registrations))
        {
            responses = await Task.WhenAll(drafts.Select(draft => PostRegistrationAsync(Registration(draft, "nb"))));
            Assert.Equal(Registrations, await barrier.ArrivedAsync());
        }

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var sent = Factory.Email.Sent;
        Assert.Equal(Registrations, sent.Count);
        Assert.All(sent, message => Assert.Equal(email, message.To));
        Assert.Single(sent, IsConfirmation);
        Assert.Equal(Registrations - 1, sent.Count(message => message.Body.Contains($"{ApiFactory.PublicBaseUrl}/login", StringComparison.Ordinal)));

        // Exactly one of the passwords logs in: the account's.
        var logins = 0;
        foreach (var draft in drafts)
        {
            using var login = await PostLoginAsync(email, draft.Password);
            logins += login.StatusCode == HttpStatusCode.NoContent ? 1 : 0;
        }

        Assert.Equal(1, logins);
    }

    // {0} is a fresh local part, {1} the same upper-cased. Each pair is the registered spelling, then
    // another spelling of it.
    [Theory]
    [Trait("SpecPartial", "S84")]
    [Trait("SpecPartial", "S87")]
    [InlineData("{0}@example.test", "  {0}@example.test\t")]
    [InlineData("{0}@example.test", "{0}@EXAMPLE.TEST")]
    [InlineData("{0}@example.test", "{1}@Example.Test")]
    [InlineData("{0}@example.test", "{0}@ｅｘａｍｐｌｅ.test")]
    [InlineData("{0}@example.test", "{0}@ＥＸＡＭＰＬＥ.test")]
    [InlineData("{0}@bølgen.test", "{0}@xn--blgen-vua.test")]
    [InlineData("{0}@xn--blgen-vua.test", "{0}@BØLGEN.test")]
    public async Task Case_and_whitespace_variants_collide(string registered, string variant)
    {
        var local = Factory.Data.Account().Email.Split('@')[0];
        var owner = Factory.Data.Account() with { Email = Factory.Data.Email(Spelling(registered, local)) };
        var other = Factory.Data.Account() with { Email = Factory.Data.Email(Spelling(variant, local)) };
        await RegisterAsync(owner);
        Factory.Email.Clear();

        await RegisterAsync(other);

        var notice = Assert.Single(Factory.Email.Sent);
        Assert.Equal(owner.Email, notice.To);
        Assert.False(IsConfirmation(notice));
        using var otherLogin = await PostLoginAsync(other.Email, other.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, otherLogin.StatusCode);
    }

    // A format character is not a letter, so a spelling with one is refused rather than becoming a
    // second account for an address IDNA would fold together.
    [Theory]
    [Trait("SpecPartial", "S84")]
    [InlineData("{0}​@example.test")]
    [InlineData("{0}@exam­ple.test")]
    [InlineData("{0}@exam‍ple.test")]
    public async Task Spelling_with_a_format_character_is_refused(string spelling)
    {
        var owner = Factory.Data.Account();
        await RegisterAsync(owner);

        using var response = await PostRegistrationAsync(
            Registration(Factory.Data.Account(), "nb") with { Email = Factory.Data.Email(Spelling(spelling, owner.Email.Split('@')[0])) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(TestContext.Current.CancellationToken);
        Assert.Equal(Account.EmailInvalidCode, body?.Code);
    }

    // S78 on the S84 normal form: any spelling of the address logs in to its account.
    [Theory]
    [Trait("Spec", "S78")]
    [InlineData("{0}@xn--blgen-vua.test")]
    [InlineData("{0}@BØLGEN.test")]
    [InlineData("{0}@ｂøｌｇｅｎ.test")]
    public async Task Login_finds_the_account_by_another_spelling(string spelling)
    {
        var local = Factory.Data.Account().Email.Split('@')[0];
        var owner = Factory.Data.Account() with { Email = Factory.Data.Email($"{local}@bølgen.test") };
        await RegisterAsync(owner);

        using var response = await PostLoginAsync(Factory.Data.Email(Spelling(spelling, local)), owner.Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Accounts registered before the normal form existed get it from the migration, and the index then
    // refuses a second account for their addresses. A label of 63 characters is the longest the
    // migration accepts. Below the API, so no Spec trait (C21.1).
    [Fact]
    [Trait("Compat", "exclude")]
    public async Task Migration_normalises_existing_addresses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(cancellationToken);
        await server.CreateDatabaseAsync("backfill", cancellationToken);
        await using var db = Context(server.ConnectionStringFor("backfill"));
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeNormalForm, cancellationToken: cancellationToken);
        var longestLabel = $"ola@{new string('B', 63)}.test";
        await InsertAccountAsync(server, "backfill", Factory.Data.Email("Ola.Nordmann@Example.TEST"), cancellationToken);
        await InsertAccountAsync(server, "backfill", Factory.Data.Email(longestLabel), cancellationToken);

        await db.Database.MigrateAsync(cancellationToken);

        var normalised = await db.Database.SqlQuery<string>($"SELECT email_normalised AS \"Value\" FROM accounts").ToListAsync(cancellationToken);
        Assert.Equal(
            new[] { "ola.nordmann@example.test", longestLabel.ToLowerInvariant() }.Order(StringComparer.Ordinal),
            normalised.Order(StringComparer.Ordinal));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAccountAsync(server, "backfill", Factory.Data.Email("ola.nordmann@example.test"), cancellationToken, "ola.nordmann@example.test"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    // SQL cannot compute IDNA or check punycode, so an address whose normal form is not lower(email), or
    // which has none, stops the migration instead of getting a wrong form. Below the API, so no Spec trait
    // (C21.1).
    public static TheoryData<string> AddressesSqlCannotNormalise => new()
    {
        "ola@bølgen.test",
        "ola@xn--zz.test",
        "ola@XN--blgen-vua.test",
        $"ola@{new string('b', 64)}.test",
    };

    [Theory]
    [Trait("Compat", "exclude")]
    [MemberData(nameof(AddressesSqlCannotNormalise))]
    public async Task Migration_stops_at_an_address_it_cannot_normalise(string email)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var server = await IsolatedPostgres.StartAsync(cancellationToken);
        await server.CreateDatabaseAsync("backfill", cancellationToken);
        await using var db = Context(server.ConnectionStringFor("backfill"));
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeNormalForm, cancellationToken: cancellationToken);
        await InsertAccountAsync(server, "backfill", Factory.Data.Email(email), cancellationToken);

        var stopped = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync(cancellationToken));

        Assert.Equal(PostgresErrorCodes.RaiseException, stopped.SqlState);
        Assert.Contains(
            await db.Database.GetPendingMigrationsAsync(cancellationToken),
            migration => migration.EndsWith("_EmailUniqueness", StringComparison.Ordinal));
    }

    private static string Spelling(string pattern, string local) =>
        string.Format(CultureInfo.InvariantCulture, pattern, local, local.ToUpperInvariant());

    private static bool IsConfirmation(EmailMessage message) =>
        message.Body.Contains($"{ApiFactory.PublicBaseUrl}/confirm#t=", StringComparison.Ordinal);

    private static string[] Names(HttpResponseMessage response) =>
        response.Headers.Select(header => header.Key)
            .Concat(response.Content.Headers.Select(header => header.Key))
            .Where(name => name != HeaderNames.Date)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ClimbOnDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<ClimbOnDbContext>().UseNpgsql(connectionString).Options);

    // An account row as the migrations before the normal form left it, or with a normal form given.
    // The address travels as a parameter, so the statement text holds no personal value (C33).
    private static async Task InsertAccountAsync(
        IsolatedPostgres server, string database, string email, CancellationToken cancellationToken, string? normalised = null)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionStringFor(database));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            normalised is null
                ? """
                  INSERT INTO accounts (id, email, password_hash, date_of_birth, language, registered_at, last_activity_at)
                  VALUES (gen_random_uuid(), @email, 'hash', '1990-05-17', 'nb', now(), now())
                  """
                : """
                  INSERT INTO accounts (id, email, password_hash, date_of_birth, language, registered_at, last_activity_at, email_normalised)
                  VALUES (gen_random_uuid(), @email, 'hash', '1990-05-17', 'nb', now(), now(), @normalised)
                  """,
            connection);
        command.Parameters.AddWithValue("email", email);
        if (normalised is not null)
        {
            command.Parameters.AddWithValue("normalised", normalised);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static RegistrationRequest Registration(AccountDraft account, string language) =>
        new(account.Email, account.Password, account.DateOfBirth, language, TermsVersion: 1, PrivacyVersion: 1);

    private Task<HttpResponseMessage> PostRegistrationAsync(RegistrationRequest request) =>
        Factory.CreateClient().PostAsJsonAsync(AccountsRoute, request, TestContext.Current.CancellationToken);

    private async Task RegisterAsync(AccountDraft account, string language = "nb")
    {
        using var response = await PostRegistrationAsync(Registration(account, language));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostLoginAsync(string email, string password) =>
        Factory.CreateClient().PostAsJsonAsync(LoginRoute, new LoginRequest(email, password), TestContext.Current.CancellationToken);

    // A row trigger that holds each account insert until the given number have arrived, and fails it if
    // they have not within ten seconds; counts the arrivals and is removed on dispose.
    private async Task<InsertBarrier> HoldAccountInsertsAsync(int arrivals)
    {
        await InsertBarrier.ExecuteAsync(
            Factory.ConnectionString,
            $$"""
            CREATE SEQUENCE account_insert_arrivals;
            CREATE FUNCTION account_insert_barrier() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE
                deadline timestamptz := clock_timestamp() + interval '10 seconds';
            BEGIN
                PERFORM nextval('account_insert_arrivals');
                WHILE (SELECT last_value FROM account_insert_arrivals) < {{arrivals}} AND clock_timestamp() < deadline LOOP
                    PERFORM pg_sleep(0.01);
                END LOOP;
                IF (SELECT last_value FROM account_insert_arrivals) < {{arrivals}} THEN
                    RAISE EXCEPTION 'account insert barrier timed out';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER account_insert_barrier BEFORE INSERT ON accounts
                FOR EACH ROW EXECUTE FUNCTION account_insert_barrier();
            """,
            TestContext.Current.CancellationToken);
        return new InsertBarrier(Factory.ConnectionString);
    }

    private sealed class InsertBarrier(string connectionString) : IAsyncDisposable
    {
        // Account inserts that reached the barrier while it was in place.
        public async Task<long> ArrivedAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand("SELECT last_value FROM account_insert_arrivals", connection);
            return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }

        public async ValueTask DisposeAsync() => await ExecuteAsync(
            connectionString,
            """
            DROP TRIGGER account_insert_barrier ON accounts;
            DROP FUNCTION account_insert_barrier();
            DROP SEQUENCE account_insert_arrivals;
            """,
            CancellationToken.None);

        public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
