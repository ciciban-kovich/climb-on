using ClimbOn.Application.Abstractions;

namespace ClimbOn.Application.Invites;

// The sweeper's step for S8 (design §4.2): writes the expiry of every invite whose timeout is due. Reads already treat
// such an invite as expired (Invite.StateAt), so this changes no answer; it only materialises the state.
public sealed class ExpireInvites(IInviteStore invites, IClock clock)
{
    // The number of invites written as expired.
    public Task<int> RunAsync(CancellationToken cancellationToken) => invites.ExpireDueAsync(clock.UtcNow, cancellationToken);
}
