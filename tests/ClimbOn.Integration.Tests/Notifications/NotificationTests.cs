using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Notifications;
using ClimbOn.Domain.Accounts;
using ClimbOn.Integration.Tests.Harness;
using ClimbOn.Integration.Tests.Invites;
using Xunit;

namespace ClimbOn.Integration.Tests.Notifications;

// In-app notifications through /api/climber/notifications (S69, S70's in-app channel, S74). Every ID stays partial:
// S70's email and push channels for these events, and S73, arrive in M2.
public sealed class NotificationTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string NotificationsRoute = "/api/climber/notifications";

    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("SpecPartial", "S69")]
    [Trait("SpecPartial", "S69.1")]
    [Trait("SpecPartial", "S70")]
    public async Task Invite_received_notifies_the_invited_climber()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();

        var invite = await climbers.InvitedAsync(a, b);

        var notification = Assert.Single(await NotificationsAsync(b));
        Assert.Equal("invite-received", notification.Event);
        Assert.Equal(a.Name, notification.DisplayName);
        Assert.Equal(invite.InviteId, notification.SubjectId);
        Assert.Equal(Factory.Clock.UtcNow, notification.CreatedAt);
        Assert.False(notification.Seen);
        Assert.Empty(await NotificationsAsync(a));
    }

    [Fact]
    [Trait("SpecPartial", "S69")]
    [Trait("SpecPartial", "S69.2")]
    [Trait("SpecPartial", "S70")]
    public async Task Invite_accepted_notifies_its_sender()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));

        using (var response = await climbers.AcceptAsync(b, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var notification = Assert.Single(await NotificationsAsync(a));
        Assert.Equal("invite-accepted", notification.Event);
        Assert.Equal(b.Name, notification.DisplayName);
        Assert.Equal(await ConnectionAsync(a), notification.SubjectId);
        Assert.Equal(Factory.Clock.UtcNow, notification.CreatedAt);

        // The recipient has only the notification of the invite.
        Assert.Equal(["invite-received"], (await NotificationsAsync(b)).Select(entry => entry.Event));
    }

    // S34: B's invite to A accepts A's pending invite, and A, its sender, is told it was accepted.
    [Fact]
    [Trait("SpecPartial", "S69")]
    [Trait("SpecPartial", "S69.2")]
    [Trait("SpecPartial", "S70")]
    public async Task Acceptance_by_a_reverse_invite_notifies_the_sender()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, b);

        var outcome = await climbers.InvitedAsync(b, a);

        Assert.Equal(InviteEndpoints.Connected, outcome.Outcome);
        var notification = Assert.Single(await NotificationsAsync(a));
        Assert.Equal("invite-accepted", notification.Event);
        Assert.Equal(b.Name, notification.DisplayName);
        Assert.Equal(await ConnectionAsync(a), notification.SubjectId);
        Assert.Equal(["invite-received"], (await NotificationsAsync(b)).Select(entry => entry.Event));
    }

    // A declined invite tells its sender nothing.
    [Fact]
    [Trait("SpecPartial", "S69")]
    public async Task Declined_invite_notifies_nobody()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = await climbers.InvitedAsync(a, b);

        using (var response = await climbers.DeclineAsync(b, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Empty(await NotificationsAsync(a));
        Assert.Single(await NotificationsAsync(b));
    }

    [Fact]
    [Trait("SpecPartial", "S69")]
    [Trait("SpecPartial", "S69.3")]
    [Trait("SpecPartial", "S70")]
    public async Task New_message_notifies_the_other_participant()
    {
        var (a, b, connection) = await ConnectedAsync();

        await SendMessageAsync(a, connection, Factory.Data.Message());

        var notification = Assert.Single(await NotificationsAsync(b), entry => entry.Event == "new-message");
        Assert.Equal(a.Name, notification.DisplayName);
        Assert.Equal(connection, notification.SubjectId);
        Assert.DoesNotContain(await NotificationsAsync(a), entry => entry.Event == "new-message");
    }

    // A message refused because its connection is not the sender's notifies nobody: the event and the message commit
    // together, or neither does.
    [Fact]
    [Trait("SpecPartial", "S69.3")]
    public async Task Refused_message_notifies_nobody()
    {
        var (a, b, connection) = await ConnectedAsync();
        var outsider = await climbers.ClimberAsync();

        using (var response = await SendAsync(outsider, connection, Factory.Data.Message()))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await SendAsync(a, connection, string.Empty))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.DoesNotContain(await NotificationsAsync(b), entry => entry.Event == "new-message");
        Assert.DoesNotContain(await NotificationsAsync(a), entry => entry.Event == "new-message");
    }

    // S74: the notification states that there is a new message and who sent it, and no part of the message.
    [Theory]
    [Trait("SpecPartial", "S74")]
    [InlineData("nb")]
    [InlineData("en")]
    public async Task Message_notification_holds_no_content(string language)
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync(language: language);
        var connection = await ConnectAsync(a, b);
        var text = Factory.Data.Message();

        await SendMessageAsync(a, connection, text);

        using var response = await GetAsync(b);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(text, body, StringComparison.Ordinal);
        var notification = Assert.Single(
            (await response.Content.ReadFromJsonAsync<NotificationListResponse>(Ct))!.Notifications,
            entry => entry.Event == "new-message");
        var expected = string.Format(
            System.Globalization.CultureInfo.GetCultureInfo(language),
            Texts.Get(NotificationEndpoints.TextKey(NotificationKind.NewMessage), Languages.FromClientSetting(language)),
            a.Name);
        Assert.Equal(expected, notification.Text);
        Assert.Equal(a.Name, notification.DisplayName);
    }

    // Each event's text exists in both languages and names the other climber; so do the code and the placeholder.
    [Fact]
    public void Texts_exist_in_both_languages_and_name_the_climber()
    {
        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            foreach (var language in new[] { Language.English, Language.Bokmal })
            {
                var text = Texts.Get(NotificationEndpoints.TextKey(kind), language);
                Assert.NotEqual(NotificationEndpoints.TextKey(kind), text);
                Assert.Contains("{0}", text, StringComparison.Ordinal);
            }
        }

        Assert.All(
            ClimberNotifications.Codes.Append(ClimberNotifications.UnnamedActorKey),
            key => Assert.All(
                new[] { Language.English, Language.Bokmal },
                language => Assert.NotEqual(key, Texts.Get(key, language))));
    }

    // Newest first; marking one seen leaves the others unseen, and marking it again changes nothing.
    [Fact]
    [Trait("SpecPartial", "S70")]
    public async Task Notifications_are_newest_first_and_marked_seen_one_by_one()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, c);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await climbers.InvitedAsync(b, c);

        var listed = await NotificationsAsync(c);
        Assert.Equal([b.Name, a.Name], listed.Select(entry => entry.DisplayName));

        for (var i = 0; i < 2; i++)
        {
            using var response = await MarkSeenAsync(c, listed[1].Id);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal([false, true], (await NotificationsAsync(c)).Select(entry => entry.Seen));
    }

    // To anyone but its recipient a notification does not exist.
    [Fact]
    [Trait("SpecPartial", "S70")]
    public async Task Another_climbers_notification_cannot_be_marked_seen()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, b);
        var notification = Assert.Single(await NotificationsAsync(b));

        foreach (var id in new[] { notification.Id, Guid.NewGuid() })
        {
            using var response = await MarkSeenAsync(a, id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ClimberNotifications.NotFoundCode, await InviteClimbers.CodeAsync(response));
        }

        Assert.False(Assert.Single(await NotificationsAsync(b)).Seen);
    }

    // A climber whose stored display name today's rules refuse is named by the neutral placeholder.
    [Fact]
    [Trait("SpecPartial", "S69.1")]
    public async Task Refused_display_name_is_replaced_by_the_placeholder()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, b);

        await climbers.RefuseProfileAsync(a);

        var notification = Assert.Single(await NotificationsAsync(b));
        Assert.Equal(Texts.Get(ClimberNotifications.UnnamedActorKey, Language.Bokmal), notification.DisplayName);
    }

    [Fact]
    public async Task Notifications_require_a_session()
    {
        using var response = await climbers.Client().GetAsync(NotificationsRoute, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.SessionRequired, await InviteClimbers.CodeAsync(response));
    }

    private async Task<(InviteClimber A, InviteClimber B, Guid Connection)> ConnectedAsync()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        return (a, b, await ConnectAsync(a, b));
    }

    private async Task<Guid> ConnectAsync(InviteClimber from, InviteClimber to)
    {
        var invite = await climbers.InvitedAsync(from, to);
        using (var response = await climbers.AcceptAsync(to, invite.InviteId!.Value))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        return await ConnectionAsync(from);
    }

    private async Task<Guid> ConnectionAsync(InviteClimber climber)
    {
        using var response = await climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/climber/connections"), climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<ConnectionListResponse>(Ct))!.Connections).Id;
    }

    private Task<HttpResponseMessage> SendAsync(InviteClimber climber, Guid connection, string text) =>
        climbers.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/api/climber/connections/{connection}/messages")
            {
                Content = JsonContent.Create(new MessageRequest(text)),
            },
            climber.Token);

    private async Task SendMessageAsync(InviteClimber climber, Guid connection, string text)
    {
        using var response = await SendAsync(climber, connection, text);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> GetAsync(InviteClimber climber) =>
        climbers.SendAsync(new HttpRequestMessage(HttpMethod.Get, NotificationsRoute), climber.Token);

    private Task<HttpResponseMessage> MarkSeenAsync(InviteClimber climber, Guid id) =>
        climbers.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{NotificationsRoute}/{id}/seen"), climber.Token);

    private async Task<IReadOnlyList<NotificationResponse>> NotificationsAsync(InviteClimber climber)
    {
        using var response = await GetAsync(climber);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationListResponse>(Ct))!.Notifications;
    }
}
