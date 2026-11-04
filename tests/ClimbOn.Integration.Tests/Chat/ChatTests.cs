using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Chat;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Chat;
using ClimbOn.Domain.Profiles;
using ClimbOn.Integration.Tests.Harness;
using ClimbOn.Integration.Tests.Invites;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Chat;

// Connections and their chat through /api/climber/connections (S9, S115, N18). A connection is made the only way
// the API makes one: an invite, accepted.
public sealed class ChatTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string ConnectionsRoute = "/api/climber/connections";

    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("SpecPartial", "S9")]
    public async Task Connected_climbers_exchange_messages()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        var first = Factory.Data.Message();
        var second = Factory.Data.Message();

        var sent = await SentAsync(a, connection, first);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await SentAsync(b, connection, second);

        Assert.True(sent.Mine);
        Assert.Equal(Factory.Clock.UtcNow.AddMinutes(-1), sent.SentAt);
        Assert.Equal([(first, true), (second, false)], (await MessagesAsync(a, connection)).Select(m => (m.Text, m.Mine)));
        Assert.Equal([(first, false), (second, true)], (await MessagesAsync(b, connection)).Select(m => (m.Text, m.Mine)));
        Assert.Equal(sent.Id, (await MessagesAsync(b, connection))[0].Id);
    }

    // Messages sent at the same instant keep the order they were sent in, also once the rows no longer lie in that
    // order on disk, as after a table rewrite by maintenance. That layout is state neither the API nor a job
    // produces, so it is arranged below the API (C21.1): the table is rewritten in sender order.
    [Fact]
    [Trait("SpecPartial", "S9")]
    public async Task Messages_keep_their_order_at_one_instant()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        var texts = Enumerable.Range(0, 5).Select(_ => Factory.Data.Message()).ToList();

        foreach (var (text, index) in texts.Select((text, index) => (text, index)))
        {
            await SentAsync(index % 2 == 0 ? a : b, connection, text);
        }

        Assert.Equal(texts, (await MessagesAsync(b, connection)).Select(message => message.Text));

        await using (var database = new NpgsqlConnection(Factory.ConnectionString))
        {
            await database.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("CLUSTER messages USING ix_messages_sender_id", database);
            await command.ExecuteNonQueryAsync(Ct);
        }

        Assert.Equal(texts, (await MessagesAsync(b, connection)).Select(message => message.Text));
    }

    // In M1 a connection is only ever active, so without one there is none: neither a pending nor a declined invite
    // makes one, and another pair's connection, or an ID that is none, does not exist to this climber.
    [Fact]
    [Trait("SpecPartial", "S9")]
    public async Task Send_without_active_connection_rejected()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var pending = await climbers.ClimberAsync();
        var declined = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        await climbers.InvitedAsync(a, pending);
        var invite = await climbers.InvitedAsync(declined, a);
        using (var response = await climbers.DeclineAsync(a, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal([b.Name], (await ConnectionsAsync(a)).Select(entry => entry.DisplayName));
        Assert.Empty(await ConnectionsAsync(pending));
        Assert.Empty(await ConnectionsAsync(declined));

        foreach (var (climber, id) in new[] { (pending, connection), (declined, connection), (a, Guid.NewGuid()) })
        {
            using var send = await SendAsync(climber, id, Factory.Data.Message());
            Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
            Assert.Equal(Messages.ConnectionNotFoundCode, await InviteClimbers.CodeAsync(send));
            using var read = await GetMessagesAsync(climber, id);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.Equal(Messages.ConnectionNotFoundCode, await InviteClimbers.CodeAsync(read));
        }

        Assert.Empty(await MessagesAsync(a, connection));
    }

    // S115: no trimming, no normalisation, no HTML; JSON is the only form the API returns it in.
    [Fact]
    [Trait("SpecPartial", "S115")]
    public async Task Content_stored_and_returned_exactly()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        var text = "  " + Factory.Data.Message("<script>alert('x')</script> &amp; <b>fet</b>") + " é \U0001F9D7\r\n\ttakk  ";

        using var send = await SendAsync(a, connection, text);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        Assert.Equal("application/json", send.Content.Headers.ContentType?.MediaType);
        Assert.Equal(text, (await send.Content.ReadFromJsonAsync<MessageResponse>(Ct))!.Text);

        foreach (var climber in new[] { a, b })
        {
            using var read = await GetMessagesAsync(climber, connection);
            Assert.Equal("application/json", read.Content.Headers.ContentType?.MediaType);
            Assert.Equal(text, Assert.Single((await read.Content.ReadFromJsonAsync<MessageListResponse>(Ct))!.Messages).Text);
        }
    }

    // S119: not blank, at most 4000 code points, text PostgreSQL can store.
    [Fact]
    [Trait("Spec", "S119")]
    public async Task Blank_overlong_or_unstorable_message_rejected()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        var longest = Factory.Data.Message(new string('x', Message.MaximumLength - 33));
        var overlong = Factory.Data.Message(new string('x', Message.MaximumLength - 32));
        var astral = Factory.Data.Message(string.Concat(Enumerable.Repeat("\U0001F9D7", Message.MaximumLength - 33)));
        Assert.Equal(Message.MaximumLength, longest.Length);

        foreach (var (text, code) in new (string?, string)[]
        {
            (null, Message.EmptyCode),
            (string.Empty, Message.EmptyCode),
            (" \r\n\t ", Message.EmptyCode),
            (overlong, Message.TooLongCode),
            (Factory.Data.Message("nul\0"), Message.InvalidCode),
        })
        {
            using var response = await SendAsync(a, connection, text);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(code, await InviteClimbers.CodeAsync(response));
        }

        // A lone surrogate, as an escape in the raw body: the JSON reader refuses it before the rule sees it.
        using (var request = new HttpRequestMessage(HttpMethod.Post, MessagesRoute(connection))
        {
            Content = new StringContent("""{"text":"x \ud800 y"}""", System.Text.Encoding.UTF8, "application/json"),
        })
        {
            using var response = await climbers.SendAsync(request, a.Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ErrorCodes.ValidationFailed, await InviteClimbers.CodeAsync(response));
        }

        Assert.Empty(await MessagesAsync(a, connection));
        await SentAsync(a, connection, longest);
        await SentAsync(a, connection, astral);
        Assert.Equal([longest, astral], (await MessagesAsync(b, connection)).Select(message => message.Text));
    }

    // N18: what the recipient does never changes what the sender is shown, and no response carries read state.
    [Fact]
    [Trait("SpecPartial", "S9")]
    public async Task Sender_never_sees_read_state()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);
        await SentAsync(a, connection, Factory.Data.Message());
        await SentAsync(b, connection, Factory.Data.Message());
        var before = await BodiesAsync(a, connection);

        Factory.Clock.Advance(TimeSpan.FromHours(2));
        var opened = await BodiesAsync(b, connection);
        var after = await BodiesAsync(a, connection);

        Assert.Equal(before.Connections, after.Connections);
        Assert.Equal(before.Messages, after.Messages);
        Assert.Equal(2, JsonDocument.Parse(before.Messages).RootElement.GetProperty("messages").GetArrayLength());
        Assert.All(
            new[] { before.Connections, before.Messages, opened.Connections, opened.Messages, after.Connections, after.Messages },
            body => Assert.DoesNotContain(Names(JsonDocument.Parse(body).RootElement), name => name.Contains("read", StringComparison.OrdinalIgnoreCase)));
    }

    // Dormant is a last activity more than 60 days ago (specs glossary); sessions last 30 days (S82), so a climber who
    // stays active logs in again. D sees B in its suggestion list only while B is not dormant.
    [Fact]
    [Trait("Spec", "S107")]
    public async Task Dormant_partner_connection_and_chat_unaffected()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var d = await climbers.ClimberAsync();
        var connection = await ConnectAsync(a, b);

        Factory.Clock.Advance(TimeSpan.FromDays(30));
        await climbers.LogInAgainAsync(a, d);
        Factory.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1));
        await climbers.LogInAgainAsync(a, d);
        Assert.Equal([a.Name], await climbers.SuggestionsAsync(d));

        var partner = Assert.Single(await ConnectionsAsync(a));
        Assert.Equal(connection, partner.Id);
        Assert.Equal("active", partner.State);
        Assert.Equal(b.Name, partner.Partner?.DisplayName);
        var fromA = Factory.Data.Message();
        await SentAsync(a, connection, fromA);

        await climbers.LogInAgainAsync(b);
        Assert.Equal([fromA], (await MessagesAsync(b, connection)).Select(message => message.Text));
        Assert.Equal(
            new[] { a.Name, b.Name }.Order(StringComparer.Ordinal),
            (await climbers.SuggestionsAsync(d)).Order(StringComparer.Ordinal));
        var fromB = Factory.Data.Message();
        await SentAsync(b, connection, fromB);
        Assert.Equal([fromA, fromB], (await MessagesAsync(a, connection)).Select(message => message.Text));
    }

    // S54: one climber's card is the same in the suggestion list, a received invite, a pending sent invite and a
    // connection. A sees rope grades in its own French scale (S13.5) and the certification with the S22 label.
    [Fact]
    [Trait("Spec", "S54")]
    [Trait("Spec", "S13.5")]
    [Trait("Spec", "S22")]
    public async Task Same_card_in_every_context()
    {
        var bouldering = new DisciplineEntryRequest("bouldering", ["indoor"], new GradeRangeRequest("5", "6B"), new GradeRangeRequest("4", "7A"));
        var french = new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest("6a", "7b"), new GradeRangeRequest("6a", "7b"));
        var lead = new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest("7-", "8"), new GradeRangeRequest("6", "8"));
        var a = await climbers.ClimberAsync(profile => profile with { RopeScale = "french", Disciplines = [bouldering, french] });
        var b = await climbers.ClimberAsync(profile => profile with { Disciplines = [bouldering, lead] });
        var c = await climbers.ClimberAsync(profile => profile with { Disciplines = [bouldering, lead], OwnGear = false });

        var suggested = await RawAsync(a, "/api/climber/suggestions");
        var cardOfB = Card(suggested.GetProperty("candidates"), b);
        var cardOfC = Card(suggested.GetProperty("candidates"), c);

        var fromB = await climbers.InvitedAsync(b, a);
        await climbers.InvitedAsync(a, c);
        var invites = await RawAsync(a, InviteClimbers.InvitesRoute);
        Assert.Equal(cardOfB, invites.GetProperty("received")[0].GetProperty("sender").GetRawText());
        Assert.Equal(cardOfC, invites.GetProperty("sent")[0].GetProperty("recipient").GetRawText());

        using (var accept = await climbers.AcceptAsync(a, fromB.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        }

        var connections = await RawAsync(a, ConnectionsRoute);
        Assert.Equal(cardOfB, connections.GetProperty("connections")[0].GetProperty("partner").GetRawText());

        var card = JsonSerializer.Deserialize<ClimberCardResponse>(cardOfB, JsonSerializerOptions.Web)!;
        Assert.Equal(
            [new GradeRangeResponse("5", "6B"), new GradeRangeResponse("6b+", "7b")],
            card.Disciplines.Select(entry => entry.Grades));
        Assert.Equal(Texts.Get(CardBuilder.SelfDeclaredKey, Language.Bokmal), card.CertificationLabel);
        Assert.False(JsonSerializer.Deserialize<ClimberCardResponse>(cardOfC, JsonSerializerOptions.Web)!.OwnGear);
    }

    // S54's exception: a partner whose stored profile today's rules refuse, as a later tightening of a rule would
    // leave it (arranged below the API), keeps the connection and its chat; they are shown by name, or the
    // placeholder, without a card. The placeholder is in the viewer's stored language, here not the default.
    [Fact]
    [Trait("Spec", "S54")]
    [Trait("SpecPartial", "S9")]
    [Trait("SpecPartial", "S105")]
    public async Task Partner_with_refused_profile_keeps_connection()
    {
        var a = await climbers.ClimberAsync(language: "en");
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var toB = await ConnectAsync(a, b);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var toC = await ConnectAsync(a, c);
        await climbers.RefuseProfileKeepingNameAsync(b);
        await climbers.RefuseProfileAsync(c);

        var list = await ConnectionsAsync(a);

        Assert.Equal(
            [(toC, Texts.Get(ListConnections.UnnamedPartnerKey, Language.English)), (toB, b.Name)],
            list.Select(entry => (entry.Id, entry.DisplayName)));
        Assert.All(list, entry => Assert.Null(entry.Partner));
        await SentAsync(a, toC, Factory.Data.Message());
    }

    // D37: the viewer's own stored profile that today's rules refuse is refused with its code, as on the suggestion
    // list and the invite list, rather than shown as a list of connections.
    [Fact]
    public async Task Own_invalid_stored_profile_is_refused_with_its_code()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await ConnectAsync(a, b);
        await climbers.RefuseProfileAsync(a);
        await Factory.Telemetry.TakeAsync();

        using (var list = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, ConnectionsRoute), a.Token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
            Assert.Equal(Profile.DisplayNameInvalidCode, await InviteClimbers.CodeAsync(list));
        }

        // Logged once per request, as on the suggestion list.
        Assert.Single(
            await Factory.Telemetry.TakeAsync(),
            item => item.Signal == "log" && Equals(item.Field("eventName"), "RequesterProfileRefused"));
    }

    [Fact]
    public void Every_chat_code_has_a_text_in_both_languages() =>
        Assert.Empty(Messages.Codes.Append(ListConnections.UnnamedPartnerKey).SelectMany(code => new[] { Language.English, Language.Bokmal }
            .Where(language => Texts.Get(code, language) == code)
            .Select(language => $"{code} ({language})")));

    private static string MessagesRoute(Guid connection) => $"{ConnectionsRoute}/{connection}/messages";

    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Names(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => [],
    };

    private static string Card(JsonElement cards, InviteClimber climber) =>
        cards.EnumerateArray().Single(card => card.GetProperty("displayName").GetString() == climber.Name).GetRawText();

    // From's invite, accepted by to; the connection's ID as from's list shows it.
    private async Task<Guid> ConnectAsync(InviteClimber from, InviteClimber to)
    {
        var invite = await climbers.InvitedAsync(from, to);
        using (var response = await climbers.AcceptAsync(to, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        return (await ConnectionsAsync(from)).Single(entry => entry.Partner?.DisplayName == to.Name).Id;
    }

    private async Task<IReadOnlyList<ConnectionResponse>> ConnectionsAsync(InviteClimber climber)
    {
        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, ConnectionsRoute), climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConnectionListResponse>(Ct))!.Connections;
    }

    private Task<HttpResponseMessage> SendAsync(InviteClimber climber, Guid connection, string? text) =>
        climbers.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, MessagesRoute(connection)) { Content = JsonContent.Create(new MessageRequest(text)) },
            climber.Token);

    private async Task<MessageResponse> SentAsync(InviteClimber climber, Guid connection, string text)
    {
        using var response = await SendAsync(climber, connection, text);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageResponse>(Ct))!;
    }

    private Task<HttpResponseMessage> GetMessagesAsync(InviteClimber climber, Guid connection) =>
        climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, MessagesRoute(connection)), climber.Token);

    private async Task<IReadOnlyList<MessageResponse>> MessagesAsync(InviteClimber climber, Guid connection)
    {
        using var response = await GetMessagesAsync(climber, connection);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageListResponse>(Ct))!.Messages;
    }

    // The connection list and the chat, as climber opens them.
    private async Task<(string Connections, string Messages)> BodiesAsync(InviteClimber climber, Guid connection)
    {
        using var connections = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, ConnectionsRoute), climber.Token);
        using var messages = await GetMessagesAsync(climber, connection);
        Assert.Equal(HttpStatusCode.OK, connections.StatusCode);
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        return (await connections.Content.ReadAsStringAsync(Ct), await messages.Content.ReadAsStringAsync(Ct));
    }

    private async Task<JsonElement> RawAsync(InviteClimber climber, string route)
    {
        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, route), climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }
}
