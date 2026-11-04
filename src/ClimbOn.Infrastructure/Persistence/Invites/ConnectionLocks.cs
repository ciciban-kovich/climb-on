using ClimbOn.Domain.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Invites;

// A connection's state belongs to Invites & Connections (design §2). Another module that must act only while a
// connection is active takes the lock through here, in its own transaction, as pair actions take PairLocks.
internal static class ConnectionLocks
{
    // Holds the connection row FOR SHARE to commit if it is active, so that no state change of the connection commits
    // before the caller's transaction does. False when it is not active.
    public static async Task<bool> ShareIfActiveAsync(ClimbOnDbContext db, Guid connectionId, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<Guid>(
                $"""SELECT id AS "Value" FROM connections WHERE id = {connectionId} AND state = {InviteNames.Of(ConnectionState.Active)} FOR SHARE""")
            .ToListAsync(cancellationToken)).Count == 1;
}
