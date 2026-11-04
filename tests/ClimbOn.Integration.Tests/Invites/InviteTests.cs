using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Invites;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Invites;
using ClimbOn.Domain.Profiles;
using ClimbOn.Integration.Tests.Harness;
using Xunit;

namespace ClimbOn.Integration.Tests.Invites;

// Sending, accepting and declining through /api/climber/invites, and the safety notice (design §2 'Invite validation
// order').
public sealed class InviteTests(ApiFactory factory) : IntegrationTest(factory)
{
    private readonly InviteClimbers climbers = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "S5")]
    public async Task Climber_invites_several_candidates()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();

        var toB = await climbers.InvitedAsync(a, b);
        var sentToB = Factory.Clock.UtcNow;
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var toC = await climbers.InvitedAsync(a, c);

        Assert.Equal(InviteEndpoints.Invited, toB.Outcome);
        Assert.Equal(InviteEndpoints.Invited, toC.Outcome);
        var sent = (await climbers.ListAsync(a)).Sent;
        Assert.Equal([toC.InviteId!.Value, toB.InviteId!.Value], sent.Select(invite => invite.Id));
        Assert.All(sent, invite => Assert.Equal("pending", invite.State));
        Assert.Equal([c.Name, b.Name], sent.Select(invite => invite.DisplayName));
        Assert.Equal([c.Name, b.Name], sent.Select(invite => invite.Recipient?.DisplayName));
        var received = Assert.Single((await climbers.ListAsync(b)).Received);
        Assert.Equal(toB.InviteId, received.Id);
        Assert.Equal(a.Name, received.Sender.DisplayName);
        Assert.Equal(sentToB, received.SentAt);
        Assert.Equal(sentToB.AddDays(7), received.ExpiresAt);
    }

    [Fact]
    [Trait("Spec", "S5")]
    public async Task Only_a_candidate_can_be_invited()
    {
        var a = await climbers.ClimberAsync();
        var elsewhere = await climbers.ClimberAsync(profile => profile with { AcceptedSexes = ["male"] });

        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, elsewhere.Id));
        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, a.Id));
        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, Guid.NewGuid()));
        Assert.Equal(ErrorCodes.ValidationFailed, await climbers.RefusedAsync(a, null));
        Assert.Equal(0, await climbers.PendingSentAsync(a));
    }

    // The candidate check is the list's own path, so a dormant climber, absent from the list, cannot be invited.
    [Fact]
    [Trait("SpecPartial", "S107")]
    [Trait("Spec", "S5")]
    public async Task Dormant_climber_cannot_be_invited()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();

        Factory.Clock.Advance(TimeSpan.FromDays(60) + TimeSpan.FromSeconds(1));
        await climbers.LogInAgainAsync(a);

        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, b.Id));
    }

    [Fact]
    [Trait("SpecPartial", "S2")]
    public async Task Unconfirmed_climber_cannot_send_or_receive_invites()
    {
        var a = await climbers.ClimberAsync();
        var unconfirmed = await climbers.ClimberAsync(confirm: false);

        using var sent = await climbers.InviteAsync(unconfirmed, a.Id);
        Assert.Equal(HttpStatusCode.Forbidden, sent.StatusCode);
        Assert.Equal(GetSuggestions.EmailNotConfirmedCode, await InviteClimbers.CodeAsync(sent));
        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, unconfirmed.Id));
        Assert.Equal(0, await climbers.PendingAsync(a, unconfirmed));

        // Answering runs the same account gate first, before the invite is looked up.
        foreach (var answer in new[] { climbers.AcceptAsync, climbers.DeclineAsync })
        {
            using var response = await answer(unconfirmed, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(GetSuggestions.EmailNotConfirmedCode, await InviteClimbers.CodeAsync(response));
        }
    }

    // The account gates run in one order on every path: S2, then S23.
    [Fact]
    [Trait("SpecPartial", "S2")]
    public async Task Email_confirmation_is_checked_before_the_safety_notice()
    {
        var a = await climbers.ClimberAsync();
        var unconfirmed = await climbers.ClimberAsync(confirm: false, acknowledge: false);

        using var sent = await climbers.InviteAsync(unconfirmed, a.Id);
        Assert.Equal(GetSuggestions.EmailNotConfirmedCode, await InviteClimbers.CodeAsync(sent));
        using var accepted = await climbers.AcceptAsync(unconfirmed, Guid.NewGuid());
        Assert.Equal(GetSuggestions.EmailNotConfirmedCode, await InviteClimbers.CodeAsync(accepted));
    }

    // The climber's own stored profile that today's rules refuse is refused with its code, as on the suggestion
    // list, rather than shown as an empty invite list or a refused candidate.
    [Fact]
    public async Task Own_invalid_stored_profile_is_refused_with_its_code()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        await climbers.InvitedAsync(b, a);
        await climbers.RefuseProfileAsync(a);
        await Factory.Telemetry.TakeAsync();

        using (var list = await climbers.GetListAsync(a))
        {
            Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
            Assert.Equal(Profile.DisplayNameInvalidCode, await InviteClimbers.CodeAsync(list));
        }

        Assert.Single(await Factory.Telemetry.TakeAsync(), IsProfileRefusal);
        Assert.Equal(Profile.DisplayNameInvalidCode, await climbers.RefusedAsync(a, c.Id));
        Assert.Single(await Factory.Telemetry.TakeAsync(), IsProfileRefusal);
        Assert.Equal(0, await climbers.PendingAsync(a, c));

        // Logged once per request, as on the suggestion list.
        static bool IsProfileRefusal(TelemetryItem item) =>
            item.Signal == "log" && Equals(item.Field("eventName"), "RequesterProfileRefused");
    }

    [Fact]
    [Trait("Spec", "S23")]
    public async Task Invite_is_refused_until_the_safety_notice_is_acknowledged()
    {
        var a = await climbers.ClimberAsync(acknowledge: false);
        var b = await climbers.ClimberAsync();

        using (var refused = await climbers.InviteAsync(a, b.Id))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(SafetyNotice.NotAcknowledgedCode, await InviteClimbers.CodeAsync(refused));
        }

        Assert.Null((await NoticeAsync(a)).AcknowledgedAt);
        var acknowledgedAt = Factory.Clock.UtcNow;
        using (var acknowledged = await climbers.AcknowledgeAsync(a))
        {
            Assert.Equal(HttpStatusCode.OK, acknowledged.StatusCode);
            Assert.Equal(acknowledgedAt, (await acknowledged.Content.ReadFromJsonAsync<SafetyNoticeResponse>(Ct))!.AcknowledgedAt);
        }

        // A second acknowledgement keeps the first's time.
        Factory.Clock.Advance(TimeSpan.FromHours(1));
        using (var again = await climbers.AcknowledgeAsync(a))
        {
            Assert.Equal(acknowledgedAt, (await again.Content.ReadFromJsonAsync<SafetyNoticeResponse>(Ct))!.AcknowledgedAt);
        }

        Assert.Equal(acknowledgedAt, (await NoticeAsync(a)).AcknowledgedAt);
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, b)).Outcome);
    }

    // S23 is an account gate, so it comes before S34 too.
    [Fact]
    [Trait("Spec", "S23")]
    [Trait("Spec", "S34")]
    public async Task Safety_notice_gates_the_S34_path_too()
    {
        var a = await climbers.ClimberAsync(acknowledge: false);
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(b, a);

        Assert.Equal(SafetyNotice.NotAcknowledgedCode, await climbers.RefusedAsync(a, b.Id));
        Assert.Equal(1, await climbers.PendingAsync(a, b));
        Assert.Equal(0, await climbers.ConnectionsAsync(a, b));
    }

    // S23 gates accepting as it gates sending; the invite waits, and the same accept succeeds once acknowledged.
    [Fact]
    [Trait("Spec", "S23")]
    [Trait("SpecPartial", "S7")]
    public async Task Accept_is_refused_until_the_safety_notice_is_acknowledged()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync(acknowledge: false);
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        using (var refused = await climbers.AcceptAsync(b, invite))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(SafetyNotice.NotAcknowledgedCode, await InviteClimbers.CodeAsync(refused));
        }

        Assert.Equal(1, await climbers.PendingAsync(a, b));
        Assert.Equal(0, await climbers.ConnectionsAsync(a, b));
        Assert.Equal(invite, Assert.Single((await climbers.ListAsync(b)).Received).Id);

        (await climbers.AcknowledgeAsync(b)).Dispose();
        using (var accepted = await climbers.AcceptAsync(b, invite))
        {
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
    }

    // Declining needs no acknowledgement.
    [Fact]
    [Trait("Spec", "S23")]
    [Trait("SpecPartial", "S7")]
    public async Task Decline_needs_no_safety_notice_acknowledgement()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync(acknowledge: false);
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        using (var declined = await climbers.DeclineAsync(b, invite))
        {
            Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        }

        Assert.Equal("not-accepted", Assert.Single((await climbers.ListAsync(a)).Sent).State);
    }

    // The notice is in the account's stored language (S105), which registration set to nb.
    [Fact]
    [Trait("Spec", "S23")]
    [Trait("SpecPartial", "S105")]
    public async Task Safety_notice_is_in_the_account_language()
    {
        var a = await climbers.ClimberAsync(acknowledge: false);

        Assert.Equal(Texts.Get(SafetyNotice.TextKey, Language.Bokmal), (await NoticeAsync(a)).Text);

        using var change = new HttpRequestMessage(HttpMethod.Put, "/api/climber/language")
        {
            Content = JsonContent.Create(new LanguageChangeRequest("en")),
        };
        change.Headers.Add("Cookie", $"{SessionCookie.Name}={a.Token}");
        using var english = await climbers.Client().SendAsync(change, Ct);
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);

        Assert.Equal(Texts.Get(SafetyNotice.TextKey, Language.English), (await NoticeAsync(a)).Text);
        Assert.NotEqual(Texts.Get(SafetyNotice.TextKey, Language.English), Texts.Get(SafetyNotice.TextKey, Language.Bokmal));
    }

    [Fact]
    [Trait("SpecPartial", "S7")]
    public async Task Recipient_accepts_and_a_connection_exists()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        using (var accepted = await climbers.AcceptAsync(b, invite))
        {
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
        Assert.Equal("accepted", Assert.Single((await climbers.ListAsync(a)).Sent).State);
        Assert.Empty((await climbers.ListAsync(b)).Received);
    }

    [Fact]
    [Trait("SpecPartial", "S7")]
    [Trait("SpecPartial", "S35")]
    public async Task Recipient_declines()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        using (var declined = await climbers.DeclineAsync(b, invite))
        {
            Assert.Equal(HttpStatusCode.NoContent, declined.StatusCode);
        }

        Assert.Equal(0, await climbers.ConnectionsAsync(a, b));
        Assert.Equal("not-accepted", Assert.Single((await climbers.ListAsync(a)).Sent).State);
        Assert.Empty((await climbers.ListAsync(b)).Received);
    }

    // Accepted, declined and expired are final: neither answer changes them.
    [Theory]
    [Trait("SpecPartial", "S7")]
    [InlineData("accepted")]
    [InlineData("declined")]
    [InlineData("expired")]
    public async Task Final_states_stay_final(string state)
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;
        switch (state)
        {
            case "accepted":
                (await climbers.AcceptAsync(b, invite)).Dispose();
                break;
            case "declined":
                (await climbers.DeclineAsync(b, invite)).Dispose();
                break;
            default:
                Factory.Clock.Advance(Invite.Lifetime);
                break;
        }

        var shown = Assert.Single((await climbers.ListAsync(a)).Sent).State;
        foreach (var answer in new[] { climbers.AcceptAsync, climbers.DeclineAsync })
        {
            using var response = await answer(b, invite);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(Invite.NotPendingCode, await InviteClimbers.CodeAsync(response));
        }

        Assert.Equal(shown, Assert.Single((await climbers.ListAsync(a)).Sent).State);
        Assert.Equal(state == "accepted" ? 1 : 0, await climbers.ConnectionsAsync(a, b));
    }

    // The sender sees the recipient's card only while the invite is pending; once it has ended, only the recipient's
    // display name and the state.
    [Theory]
    [Trait("SpecPartial", "S35")]
    [InlineData("accepted")]
    [InlineData("declined")]
    [InlineData("expired")]
    public async Task Sender_sees_the_recipient_card_only_while_pending(string state)
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        var pending = Assert.Single((await climbers.ListAsync(a)).Sent);
        Assert.Equal(b.Name, pending.DisplayName);
        Assert.Equal(b.Name, pending.Recipient?.DisplayName);

        switch (state)
        {
            case "accepted":
                (await climbers.AcceptAsync(b, invite)).Dispose();
                break;
            case "declined":
                (await climbers.DeclineAsync(b, invite)).Dispose();
                break;
            default:
                Factory.Clock.Advance(Invite.Lifetime);
                break;
        }

        var ended = Assert.Single((await climbers.ListAsync(a)).Sent);
        Assert.NotEqual("pending", ended.State);
        Assert.Equal(b.Name, ended.DisplayName);
        Assert.Null(ended.Recipient);
    }

    // A sent invite is never dropped from the sender's list. A recipient whose stored profile today's rules refuse is
    // named as stored when the display name alone passes them, otherwise by a neutral placeholder, with the state and
    // never a card. The placeholder is in the sender's stored language, here not the default.
    [Fact]
    [Trait("SpecPartial", "S35")]
    [Trait("SpecPartial", "S105")]
    public async Task Sent_invite_to_a_refused_profile_stays_listed()
    {
        var a = await climbers.ClimberAsync(language: "en");
        var named = await climbers.ClimberAsync();
        var unnamed = await climbers.ClimberAsync();
        var declined = await climbers.ClimberAsync();
        var toNamed = (await climbers.InvitedAsync(a, named)).InviteId!.Value;
        var toUnnamed = (await climbers.InvitedAsync(a, unnamed)).InviteId!.Value;
        var toDeclined = (await climbers.InvitedAsync(a, declined)).InviteId!.Value;
        (await climbers.DeclineAsync(declined, toDeclined)).Dispose();
        await climbers.RefuseProfileKeepingNameAsync(named);
        await climbers.RefuseProfileAsync(unnamed);
        await climbers.RefuseProfileAsync(declined);

        var sent = (await climbers.ListAsync(a)).Sent.ToDictionary(invite => invite.Id);

        var placeholder = Texts.Get(ListInvites.UnnamedRecipientKey, Language.English);
        Assert.Equal(3, sent.Count);
        Assert.Equal(("pending", named.Name), (sent[toNamed].State, sent[toNamed].DisplayName));
        Assert.Equal(("pending", placeholder), (sent[toUnnamed].State, sent[toUnnamed].DisplayName));
        Assert.Equal(("not-accepted", placeholder), (sent[toDeclined].State, sent[toDeclined].DisplayName));
        Assert.All(sent.Values, invite => Assert.Null(invite.Recipient));
        Assert.NotEqual(placeholder, named.Name);
    }

    // Only the recipient answers an invite; to anyone else it does not exist.
    [Fact]
    [Trait("SpecPartial", "S7")]
    public async Task Only_the_recipient_can_answer()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;

        foreach (var (who, id) in new[] { (a, invite), (c, invite), (b, Guid.NewGuid()) })
        {
            using var response = await climbers.AcceptAsync(who, id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AnswerInvite.NotFoundCode, await InviteClimbers.CodeAsync(response));
        }

        Assert.Equal(1, await climbers.PendingAsync(a, b));
    }

    [Fact]
    [Trait("Spec", "S8")]
    [Trait("SpecPartial", "S35")]
    public async Task Invite_expires_7_days_after_it_was_sent()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var toB = (await climbers.InvitedAsync(a, b)).InviteId!.Value;
        var toC = (await climbers.InvitedAsync(a, c)).InviteId!.Value;

        Factory.Clock.Advance(Invite.Lifetime - TimeSpan.FromSeconds(1));
        Assert.Single((await climbers.ListAsync(b)).Received);
        using (var accepted = await climbers.AcceptAsync(c, toC))
        {
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty((await climbers.ListAsync(b)).Received);
        Assert.Equal(
            ["accepted", "not-accepted"],
            (await climbers.ListAsync(a)).Sent.OrderBy(invite => invite.Id == toB).Select(invite => invite.State));
        using var late = await climbers.AcceptAsync(b, toB);
        Assert.Equal(Invite.NotPendingCode, await InviteClimbers.CodeAsync(late));
        Assert.Equal(0, await climbers.ConnectionsAsync(a, b));
    }

    // The sender cannot tell a declined invite from an expired one (S35): same state, same fields.
    [Fact]
    [Trait("SpecPartial", "S35")]
    public async Task Declined_and_expired_look_the_same_to_the_sender()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var toB = (await climbers.InvitedAsync(a, b)).InviteId!.Value;
        await climbers.InvitedAsync(a, c);
        (await climbers.DeclineAsync(b, toB)).Dispose();

        Factory.Clock.Advance(Invite.Lifetime);

        var sent = (await climbers.ListAsync(a)).Sent;
        Assert.Equal(2, sent.Count);
        Assert.All(sent, invite => Assert.Equal("not-accepted", invite.State));
        Assert.All(sent, invite => Assert.Null(invite.Recipient));
    }

    [Fact]
    [Trait("Spec", "S31")]
    public async Task Eleventh_pending_invite_is_refused()
    {
        var a = await climbers.ClimberAsync();
        var others = await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent + 1);
        var invites = new List<Guid>();
        foreach (var other in others.Take(InvitePolicy.MaxPendingSent))
        {
            invites.Add((await climbers.InvitedAsync(a, other)).InviteId!.Value);
        }

        Assert.Equal(InvitePolicy.LimitReachedCode, await climbers.RefusedAsync(a, others[^1].Id));

        // An answered invite is no longer pending, so it frees its place.
        (await climbers.DeclineAsync(others[0], invites[0])).Dispose();
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, others[^1])).Outcome);
        Assert.Equal(InvitePolicy.MaxPendingSent, await climbers.PendingSentAsync(a));
    }

    // Incoming invites never count toward the limit.
    [Fact]
    [Trait("Spec", "S31")]
    public async Task Received_invites_do_not_count_toward_limit()
    {
        var a = await climbers.ClimberAsync();
        foreach (var other in await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent))
        {
            await climbers.InvitedAsync(other, a);
        }

        var b = await climbers.ClimberAsync();
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, b)).Outcome);
    }

    [Fact]
    [Trait("Spec", "S31")]
    [Trait("Spec", "S8")]
    public async Task Timed_out_invites_do_not_count_toward_limit()
    {
        var a = await climbers.ClimberAsync();
        foreach (var other in await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent))
        {
            await climbers.InvitedAsync(a, other);
        }

        var b = await climbers.ClimberAsync();
        Factory.Clock.Advance(Invite.Lifetime - TimeSpan.FromSeconds(1));
        Assert.Equal(InvitePolicy.LimitReachedCode, await climbers.RefusedAsync(a, b.Id));

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, b)).Outcome);
    }

    [Fact]
    [Trait("Spec", "S32")]
    public async Task Second_invite_to_the_same_climber_is_refused_while_pending()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, b);

        Assert.Equal(InvitePolicy.AlreadyPendingCode, await climbers.RefusedAsync(a, b.Id));
        Assert.Equal(1, await climbers.PendingAsync(a, b));
    }

    [Fact]
    [Trait("SpecPartial", "S33")]
    public async Task Decline_bars_the_sender_for_30_days_but_not_the_recipient()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var invite = (await climbers.InvitedAsync(a, b)).InviteId!.Value;
        (await climbers.DeclineAsync(b, invite)).Dispose();

        Assert.Equal(InvitePolicy.CooldownCode, await climbers.RefusedAsync(a, b.Id));

        // The recipient may invite the sender at once.
        var back = (await climbers.InvitedAsync(b, a)).InviteId!.Value;
        (await climbers.DeclineAsync(a, back)).Dispose();

        Factory.Clock.Advance(InvitePolicy.Cooldown - TimeSpan.FromSeconds(1));
        await climbers.LogInAgainAsync(a, b);
        Assert.Equal(InvitePolicy.CooldownCode, await climbers.RefusedAsync(a, b.Id));

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, b)).Outcome);
    }

    // A timed-out invite ends 7 days after it was sent; the 30 days run from then.
    [Fact]
    [Trait("SpecPartial", "S33")]
    [Trait("Spec", "S8")]
    public async Task Timeout_bars_the_sender_for_30_days()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(a, b);

        Factory.Clock.Advance(Invite.Lifetime);
        Assert.Equal(InvitePolicy.CooldownCode, await climbers.RefusedAsync(a, b.Id));

        Factory.Clock.Advance(InvitePolicy.Cooldown - TimeSpan.FromSeconds(1));
        await climbers.LogInAgainAsync(a, b);
        Assert.Equal(InvitePolicy.CooldownCode, await climbers.RefusedAsync(a, b.Id));

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(InviteEndpoints.Invited, (await climbers.InvitedAsync(a, b)).Outcome);
        Assert.Equal(1, await climbers.PendingAsync(a, b));
    }

    [Fact]
    [Trait("Spec", "S34")]
    public async Task Inviting_a_climber_who_invited_you_connects_you()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(b, a);

        var result = await climbers.InvitedAsync(a, b);

        Assert.Equal(InviteEndpoints.Connected, result.Outcome);
        Assert.Null(result.InviteId);
        Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
        Assert.Equal(0, await climbers.PendingAsync(a, b));
        Assert.Empty((await climbers.ListAsync(a)).Sent);
        Assert.Equal("accepted", Assert.Single((await climbers.ListAsync(b)).Sent).State);
    }

    // S34 comes before the candidate check: b's pending invite connects the pair even once b no longer accepts a, so
    // that a's invite would otherwise be refused as not a candidate.
    [Fact]
    [Trait("Spec", "S34")]
    public async Task Reverse_invite_connects_even_when_no_longer_a_candidate()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        await climbers.InvitedAsync(b, a);
        await climbers.SaveProfileAsync(b, profile => profile with { AcceptedSexes = ["male"] });

        var result = await climbers.InvitedAsync(a, b);

        Assert.Equal(InviteEndpoints.Connected, result.Outcome);
        Assert.Equal(1, await climbers.ConnectionsAsync(a, b));
        Assert.Equal(0, await climbers.PendingAsync(a, b));
    }

    // Each step is checked against a state that every later step would also refuse, so the code shows which one
    // ran first: S34 before every rule after it; the candidate check before the connection check; that before S32,
    // S33 and S31; S32 and S33 before S31.
    [Fact]
    [Trait("Spec", "S34")]
    [Trait("Spec", "S31")]
    [Trait("Spec", "S32")]
    [Trait("SpecPartial", "S33")]
    public async Task Validation_order_is_S34_candidate_connection_then_limits()
    {
        var a = await climbers.ClimberAsync();
        var b = await climbers.ClimberAsync();
        var c = await climbers.ClimberAsync();
        var d = await climbers.ClimberAsync();
        var e = await climbers.ClimberAsync();

        // a has invited b, which b declines: a is barred from inviting b (S33). a fills its limit, c among them (S31).
        (await climbers.DeclineAsync(b, (await climbers.InvitedAsync(a, b)).InviteId!.Value)).Dispose();
        await climbers.InvitedAsync(a, c);
        foreach (var other in await climbers.ClimbersAsync(InvitePolicy.MaxPendingSent - 1))
        {
            await climbers.InvitedAsync(a, other);
        }

        // S32 before S31, S33 before S31.
        Assert.Equal(InvitePolicy.AlreadyPendingCode, await climbers.RefusedAsync(a, c.Id));
        Assert.Equal(InvitePolicy.CooldownCode, await climbers.RefusedAsync(a, b.Id));
        Assert.Equal(InvitePolicy.LimitReachedCode, await climbers.RefusedAsync(a, e.Id));

        // S34 before S31 and S33: b, unrestricted, invites a; a's invite to b accepts it.
        await climbers.InvitedAsync(b, a);
        Assert.Equal(InviteEndpoints.Connected, (await climbers.InvitedAsync(a, b)).Outcome);

        // The connection check before S31 and S33.
        Assert.Equal(InvitePolicy.AlreadyConnectedCode, await climbers.RefusedAsync(a, b.Id));

        // The candidate check before the connection check: b stops accepting a's sex.
        await climbers.SaveProfileAsync(b, profile => profile with { AcceptedSexes = ["male"] });
        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, b.Id));

        // And before S31: d is no candidate either.
        await climbers.SaveProfileAsync(d, profile => profile with { AcceptedSexes = ["male"] });
        Assert.Equal(InvitePolicy.NotCandidateCode, await climbers.RefusedAsync(a, d.Id));
    }

    // C46 checks the two files agree; this checks the codes are among their keys.
    [Fact]
    public void Every_invite_text_exists_in_both_languages() =>
        Assert.Empty(SendInvite.Codes.Concat(AnswerInvite.Codes).Append(SafetyNotice.TextKey)
            .SelectMany(code => new[] { Language.English, Language.Bokmal }
                .Where(language => Texts.Get(code, language) == code)
                .Select(language => $"{code} ({language})")));

    private async Task<SafetyNoticeResponse> NoticeAsync(InviteClimber climber)
    {
        using var response = await climbers.GetSafetyNoticeAsync(climber);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SafetyNoticeResponse>(Ct))!;
    }
}
