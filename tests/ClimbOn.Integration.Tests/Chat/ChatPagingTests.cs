using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Application.Chat;
using ClimbOn.Integration.Tests.Harness;
using ClimbOn.Integration.Tests.Invites;
using Xunit;

namespace ClimbOn.Integration.Tests.Chat;

// The messages of a connection, page by page through GET /api/climber/connections/{id}/messages (S118, API half).
public sealed class ChatPagingTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string ConnectionsRoute = "/api/climber/connections";

    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("SpecPartial", "S118")]
    public async Task Default_page_is_newest_10()
    {
        var (a, b, connection) = await ConnectedAsync();
        var texts = await SendManyAsync(a, b, connection, 15, tied: _ => false);

        var page = await PageAsync(b, connection, before: null);

        Assert.Equal(texts[5..], page.Messages.Select(message => message.Text));
        Assert.True(page.HasOlder);
    }

    // Two groups sent at one instant each, both straddling a page boundary: 25 messages page as 15-24, 5-14 and 0-4.
    [Fact]
    [Trait("SpecPartial", "S118")]
    public async Task Walk_back_reaches_first_message_once_with_tied_sent_at()
    {
        var (a, b, connection) = await ConnectedAsync();
        var texts = await SendManyAsync(a, b, connection, 25, tied: index => index is >= 3 and <= 6 or >= 12 and <= 17);

        var pages = new List<MessageListResponse> { await PageAsync(a, connection, before: null) };
        while (pages[^1].HasOlder)
        {
            Assert.True(pages.Count < 5, "the walk back does not end");
            pages.Add(await PageAsync(a, connection, before: pages[^1].Messages[0].Id.ToString()));
        }

        var walked = pages.AsEnumerable().Reverse().SelectMany(page => page.Messages).ToList();
        Assert.Equal([10, 10, 5], pages.Select(page => page.Messages.Count));
        Assert.Equal(texts, walked.Select(message => message.Text));
        Assert.Equal(walked.Count, walked.Select(message => message.Id).Distinct().Count());
        Assert.Equal(walked[14].SentAt, walked[15].SentAt);
        Assert.Equal(walked[4].SentAt, walked[5].SentAt);
    }

    // Exactly two full pages: the second is the oldest, so it says no older messages exist, however full it is.
    [Fact]
    [Trait("SpecPartial", "S118")]
    public async Task Oldest_page_signals_no_older_messages()
    {
        var (a, b, connection) = await ConnectedAsync();
        var texts = await SendManyAsync(a, b, connection, 20, tied: _ => false);

        var newest = await PageAsync(b, connection, before: null);
        var oldest = await PageAsync(b, connection, before: newest.Messages[0].Id.ToString());

        Assert.True(newest.HasOlder);
        Assert.Equal(texts[..10], oldest.Messages.Select(message => message.Text));
        Assert.False(oldest.HasOlder);
    }

    [Theory]
    [Trait("SpecPartial", "S118")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(Messages.PageSize)]
    public async Task Ten_or_fewer_messages_fit_on_first_page(int count)
    {
        var (a, b, connection) = await ConnectedAsync();
        var texts = await SendManyAsync(a, b, connection, count, tied: _ => false);

        var page = await PageAsync(a, connection, before: null);

        Assert.Equal(texts, page.Messages.Select(message => message.Text));
        Assert.False(page.HasOlder);
    }

    // A cursor that is malformed, a message of another connection or no message at all is refused with one code, so
    // the reply does not show whether a foreign message exists.
    [Fact]
    [Trait("SpecPartial", "S118")]
    public async Task Malformed_foreign_or_unknown_cursor_refused_with_one_code()
    {
        var (a, b, connection) = await ConnectedAsync();
        var (c, d, other) = await ConnectedAsync();
        await SendManyAsync(a, b, connection, 3, tied: _ => false);
        await SendManyAsync(c, d, other, 3, tied: _ => false);
        var foreign = (await PageAsync(c, other, before: null)).Messages[1].Id;

        foreach (var cursor in new[] { "", "x", "123", Guid.NewGuid() + "x", foreign.ToString(), Guid.NewGuid().ToString() })
        {
            using var response = await GetAsync(a, connection, cursor);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(Messages.MessageCursorInvalidCode, await InviteClimbers.CodeAsync(response));
        }
    }

    // The connection is decided before the cursor: to a climber outside it, it does not exist whatever the cursor.
    [Fact]
    [Trait("SpecPartial", "S118")]
    public async Task Outsider_gets_not_found_whatever_the_cursor()
    {
        var (a, b, connection) = await ConnectedAsync();
        var outsider = await climbers.ClimberAsync();
        await SendManyAsync(a, b, connection, 3, tied: _ => false);
        var own = (await PageAsync(a, connection, before: null)).Messages[1].Id;

        foreach (var cursor in new[] { own.ToString(), Guid.NewGuid().ToString(), "x" })
        {
            using var response = await GetAsync(outsider, connection, cursor);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(Messages.ConnectionNotFoundCode, await InviteClimbers.CodeAsync(response));
        }
    }

    private static string MessagesRoute(Guid connection) => $"{ConnectionsRoute}/{connection}/messages";

    // Two climbers and their connection: an invite, accepted.
    private async Task<(InviteClimber A, InviteClimber B, Guid Connection)> ConnectedAsync()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);
        using (var accept = await climbers.AcceptAsync(b, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        }

        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, ConnectionsRoute), a.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var connection = Assert.Single((await response.Content.ReadFromJsonAsync<ConnectionListResponse>(Ct))!.Connections);
        return (a, b, connection.Id);
    }

    // Count messages, alternating senders; message index is sent at the same instant as the one before it when tied
    // says so, otherwise a second later.
    private async Task<List<string>> SendManyAsync(
        InviteClimber a, InviteClimber b, Guid connection, int count, Func<int, bool> tied)
    {
        var texts = new List<string>();
        for (var index = 0; index < count; index++)
        {
            if (!tied(index))
            {
                Factory.Clock.Advance(TimeSpan.FromSeconds(1));
            }

            var text = Factory.Data.Message();
            using var response = await climbers.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, MessagesRoute(connection)) { Content = JsonContent.Create(new MessageRequest(text)) },
                (index % 2 == 0 ? a : b).Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            texts.Add(text);
        }

        return texts;
    }

    private Task<HttpResponseMessage> GetAsync(InviteClimber climber, Guid connection, string? before) =>
        climbers.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                before is null ? MessagesRoute(connection) : $"{MessagesRoute(connection)}?before={Uri.EscapeDataString(before)}"),
            climber.Token);

    private async Task<MessageListResponse> PageAsync(InviteClimber climber, Guid connection, string? before)
    {
        using var response = await GetAsync(climber, connection, before);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageListResponse>(Ct))!;
    }
}
