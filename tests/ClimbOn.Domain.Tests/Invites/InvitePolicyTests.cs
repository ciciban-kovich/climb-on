using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Invites;
using Xunit;

namespace ClimbOn.Domain.Tests.Invites;

public sealed class InvitePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    [Trait("Spec", "S34")]
    public void Pending_invite_from_the_recipient_is_accepted_instead()
    {
        var reverse = Invite.Send(Guid.NewGuid(), B, A, Now.AddDays(-1));

        Assert.Equal(reverse, InvitePolicy.ToAcceptInstead(A, B, [reverse], Now));
        Assert.Null(InvitePolicy.ToAcceptInstead(B, A, [reverse], Now));
    }

    [Fact]
    [Trait("Spec", "S34")]
    [Trait("Spec", "S8")]
    public void Timed_out_invite_from_the_recipient_is_not_accepted_instead() =>
        Assert.Null(InvitePolicy.ToAcceptInstead(A, B, [Invite.Send(Guid.NewGuid(), B, A, Now - Invite.Lifetime)], Now));

    // Each case breaks every rule from the one named on; the first of them in the design order is reported.
    [Theory]
    [Trait("Spec", "S31")]
    [Trait("Spec", "S32")]
    [Trait("SpecPartial", "S33")]
    [InlineData(false, true, true, true, 10, InvitePolicy.NotCandidateCode)]
    [InlineData(true, true, true, true, 10, InvitePolicy.AlreadyConnectedCode)]
    [InlineData(true, false, true, true, 10, InvitePolicy.AlreadyPendingCode)]
    [InlineData(true, false, false, true, 10, InvitePolicy.CooldownCode)]
    [InlineData(true, false, false, false, 10, InvitePolicy.LimitReachedCode)]
    public void Rules_are_checked_in_order(
        bool candidate, bool connected, bool pending, bool declined, int pendingSent, string code)
    {
        var invites = new List<Invite>();
        if (pending)
        {
            invites.Add(Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-1)));
        }

        if (declined)
        {
            invites.Add(Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-3)).Decline(Now.AddDays(-2)));
        }

        var error = Assert.Throws<DomainError>(() =>
            InvitePolicy.EnsureMayInvite(A, B, candidate, connected, invites, pendingSent, Now));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    [Trait("Spec", "S31")]
    public void Nine_pending_leave_room_for_a_tenth() =>
        InvitePolicy.EnsureMayInvite(A, B, isCandidate: true, connected: false, [], pendingSent: 9, Now);

    [Theory]
    [Trait("SpecPartial", "S33")]
    [InlineData(-30, false)]
    [InlineData(-29.99, true)]
    [InlineData(0, true)]
    public void Decline_bars_its_sender_for_30_days(double declinedDaysAgo, bool barred)
    {
        var declined = Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(declinedDaysAgo - 1)).Decline(Now.AddDays(declinedDaysAgo));

        Assert.Equal(barred, InvitePolicy.CoolsDownAt(declined, Now));
    }

    // Expiry by timeout ends the invite 7 days after it was sent, so the bar lasts until 37 days after.
    [Theory]
    [Trait("SpecPartial", "S33")]
    [Trait("Spec", "S8")]
    [InlineData(37, false)]
    [InlineData(36.99, true)]
    [InlineData(7, true)]
    [InlineData(6.99, false)]
    public void Timeout_bars_its_sender_for_30_days_after_expiry(double sentDaysAgo, bool barred) =>
        Assert.Equal(barred, InvitePolicy.CoolsDownAt(Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-sentDaysAgo)), Now));

    [Theory]
    [Trait("SpecPartial", "S33")]
    [InlineData(ExpiryCause.Deletion, false)]
    [InlineData(ExpiryCause.Block, true)]
    [InlineData(ExpiryCause.Suspension, true)]
    [InlineData(ExpiryCause.Timeout, true)]
    public void Every_expiry_but_deletion_bars_its_sender(ExpiryCause cause, bool barred)
    {
        var expired = Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-2)) with
        {
            StoredState = InviteState.Expired,
            Cause = cause,
            EndedAt = Now.AddDays(-1),
        };

        Assert.Equal(barred, InvitePolicy.CoolsDownAt(expired, Now));
    }

    [Fact]
    [Trait("SpecPartial", "S33")]
    public void Recipient_of_a_declined_invite_is_not_barred()
    {
        var declined = Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-2)).Decline(Now.AddDays(-1));

        InvitePolicy.EnsureMayInvite(B, A, isCandidate: true, connected: false, [declined], pendingSent: 0, Now);
    }

    [Fact]
    [Trait("SpecPartial", "S33")]
    public void Accepted_invite_does_not_bar() =>
        Assert.False(InvitePolicy.CoolsDownAt(Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-2)).Accept(Now.AddDays(-1)), Now));

    [Theory]
    [Trait("Spec", "S8")]
    [InlineData(-7, InviteState.Expired)]
    [InlineData(-6.99, InviteState.Pending)]
    public void Pending_invite_expires_7_days_after_it_was_sent(double sentDaysAgo, InviteState state) =>
        Assert.Equal(state, Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(sentDaysAgo)).StateAt(Now));

    [Fact]
    [Trait("SpecPartial", "S7")]
    public void Final_states_do_not_change()
    {
        var sent = Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-1));
        Invite[] finals = [sent.Accept(Now), sent.Decline(Now), sent with { SentAt = Now - Invite.Lifetime }];

        foreach (var final in finals)
        {
            Assert.Equal(Invite.NotPendingCode, Assert.Throws<DomainError>(() => final.Accept(Now.AddSeconds(1))).Code);
            Assert.Equal(Invite.NotPendingCode, Assert.Throws<DomainError>(() => final.Decline(Now.AddSeconds(1))).Code);
            Assert.NotEqual(InviteState.Pending, final.StateAt(Now.AddYears(1)));
        }
    }

    [Theory]
    [Trait("SpecPartial", "S35")]
    [InlineData(InviteState.Declined)]
    [InlineData(InviteState.Expired)]
    public void Sender_sees_declined_and_expired_alike(InviteState state)
    {
        var sent = Invite.Send(Guid.NewGuid(), A, B, Now.AddDays(-1));
        var ended = state == InviteState.Declined ? sent.Decline(Now) : sent with { SentAt = Now - Invite.Lifetime };

        Assert.Equal(SentInviteView.NotAccepted, ended.SenderViewAt(Now));
    }
}
