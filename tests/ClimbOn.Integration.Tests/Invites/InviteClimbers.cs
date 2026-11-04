using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Domain.Profiles;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Invites;

// A climber made through the API. Token is the current session; sessions last 30 days (S82), so a test that moves
// the clock further logs in again (InviteClimbers.LogInAgainAsync).
public sealed class InviteClimber(AccountDraft account, Guid id, string token)
{
    public AccountDraft Account { get; } = account;

    public Guid Id { get; } = id;

    public string Token { get; set; } = token;

    public string Name => Account.DisplayName;
}

// The invite API as climbers call it. Every climber is built from TestData.Profile, so any two match unless a test
// changes that.
public sealed class InviteClimbers(ApiFactory factory)
{
    public const string InvitesRoute = "/api/climber/invites";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Registered in language, confirmed unless confirm is false, logged in, with TestData.Profile changed by change,
    // and the safety notice acknowledged unless acknowledge is false.
    public async Task<InviteClimber> ClimberAsync(
        Func<ProfileRequest, ProfileRequest>? change = null, bool confirm = true, bool acknowledge = true, string language = "nb")
    {
        var account = factory.Data.Account();
        using (var response = await Client().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, language, TermsVersion: 1, PrivacyVersion: 1),
            Ct))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        if (confirm)
        {
            await ConfirmAsync(account);
        }

        var climber = new InviteClimber(account, await IdAsync(account), await LogInAsync(account));
        await SaveProfileAsync(climber, change ?? (profile => profile));
        if (acknowledge)
        {
            using var response = await AcknowledgeAsync(climber);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return climber;
    }

    public async Task<List<InviteClimber>> ClimbersAsync(int count)
    {
        var climbers = new List<InviteClimber>();
        for (var i = 0; i < count; i++)
        {
            climbers.Add(await ClimberAsync());
        }

        return climbers;
    }

    public async Task SaveProfileAsync(InviteClimber climber, Func<ProfileRequest, ProfileRequest> change)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/climber/profile")
        {
            Content = JsonContent.Create(change(factory.Data.Profile(climber.Account))),
        };
        using var response = await SendAsync(request, climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task LogInAgainAsync(params InviteClimber[] climbers)
    {
        foreach (var climber in climbers)
        {
            climber.Token = await LogInAsync(climber.Account);
        }
    }

    public Task<HttpResponseMessage> GetSafetyNoticeAsync(InviteClimber climber) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/safety-notice"), climber.Token);

    public Task<HttpResponseMessage> AcknowledgeAsync(InviteClimber climber) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/climber/safety-notice/acknowledgement"), climber.Token);

    public Task<HttpResponseMessage> InviteAsync(InviteClimber from, Guid? to) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, InvitesRoute) { Content = JsonContent.Create(new InviteRequest(to)) },
            from.Token);

    // An invite the API accepted, with its outcome.
    public async Task<InviteResultResponse> InvitedAsync(InviteClimber from, InviteClimber to)
    {
        using var response = await InviteAsync(from, to.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InviteResultResponse>(Ct))!;
    }

    // An invite the API refused, with the refusal's code.
    public async Task<string?> RefusedAsync(InviteClimber from, Guid? to)
    {
        using var response = await InviteAsync(from, to);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        return await CodeAsync(response);
    }

    public Task<HttpResponseMessage> AcceptAsync(InviteClimber climber, Guid invite) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{InvitesRoute}/{invite}/accept"), climber.Token);

    public Task<HttpResponseMessage> DeclineAsync(InviteClimber climber, Guid invite) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{InvitesRoute}/{invite}/decline"), climber.Token);

    public Task<HttpResponseMessage> GetListAsync(InviteClimber climber) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, InvitesRoute), climber.Token);

    public async Task<InviteListResponse> ListAsync(InviteClimber climber)
    {
        using var response = await GetListAsync(climber);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InviteListResponse>(Ct))!;
    }

    // A stored profile that today's rules refuse, as a later tightening of a rule would leave it, arranged below the
    // API as the suggestion-list tests arrange it: a display name past the maximum length.
    public async Task RefuseProfileAsync(InviteClimber climber)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("UPDATE profiles SET display_name = @name WHERE account_id = @id", connection);
        command.Parameters.AddWithValue("name", new string('x', Profile.MaximumDisplayNameLength + 1));
        command.Parameters.AddWithValue("id", climber.Id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(Ct));
    }

    // Leaves the stored profile one today's rules refuse (windows_required) while its display name still passes.
    public async Task RefuseProfileKeepingNameAsync(InviteClimber climber)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("DELETE FROM availability_windows WHERE account_id = @id", connection);
        command.Parameters.AddWithValue("id", climber.Id);
        Assert.True(await command.ExecuteNonQueryAsync(Ct) > 0);
    }

    public async Task<List<string>> SuggestionsAsync(InviteClimber climber)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/suggestions"), climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<SuggestionListResponse>(Ct))!.Candidates.Select(card => card.DisplayName)];
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code;

    // Read below the API: the API has no call that counts these.
    public Task<long> ConnectionsAsync(InviteClimber a, InviteClimber b) => CountAsync(
        "SELECT count(*) FROM connections WHERE (pair_low = @a AND pair_high = @b) OR (pair_low = @b AND pair_high = @a)",
        ("a", a.Id),
        ("b", b.Id));

    public Task<long> PendingAsync(InviteClimber a, InviteClimber b) => CountAsync(
        "SELECT count(*) FROM invites WHERE state = 'pending' AND ((sender_id = @a AND recipient_id = @b) OR (sender_id = @b AND recipient_id = @a))",
        ("a", a.Id),
        ("b", b.Id));

    public Task<long> PendingSentAsync(InviteClimber a) =>
        CountAsync("SELECT count(*) FROM invites WHERE state = 'pending' AND sender_id = @a", ("a", a.Id));

    public HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task<long> CountAsync(string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<Guid> IdAsync(AccountDraft account)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id FROM accounts WHERE email = @email", connection);
        command.Parameters.AddWithValue("email", account.Email);
        return (Guid)(await command.ExecuteScalarAsync(Ct))!;
    }

    // Through the link in the confirmation email (S49).
    private async Task ConfirmAsync(AccountDraft account)
    {
        var email = factory.Email.Sent.Last(message => message.To == account.Email);
        var start = email.Body.IndexOf("#t=", StringComparison.Ordinal) + "#t=".Length;
        var end = email.Body.IndexOfAny([' ', '\r', '\n'], start);
        var token = end < 0 ? email.Body[start..] : email.Body[start..end];
        using var response = await Client().PostAsJsonAsync("/api/climber/email-confirmations", new EmailConfirmationRequest(token), Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<string> LogInAsync(AccountDraft account)
    {
        using var login = await Client().PostAsJsonAsync("/api/climber/login", new LoginRequest(account.Email, account.Password), Ct);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var token = SetCookieHeaderValue.ParseList([.. login.Headers.GetValues(HeaderNames.SetCookie)])
            .Single(cookie => cookie.Name == SessionCookie.Name).Value.Value!;
        factory.Fixtures.Register("session-token", token);
        return token;
    }

    // The request with the session cookie for token; disposes the request.
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string token)
    {
        using (request)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{SessionCookie.Name}={token}");
            return await Client().SendAsync(request, Ct);
        }
    }
}
