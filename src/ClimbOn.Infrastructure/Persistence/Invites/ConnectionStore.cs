using ClimbOn.Application.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Invites;

public sealed class ConnectionStore(ClimbOnDbContext db) : IConnectionStore
{
    public async Task<IReadOnlyList<Connection>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await db.Connections.AsNoTracking()
            .Where(row => row.PairLow == accountId || row.PairHigh == accountId)
            .ToListAsync(cancellationToken))
        .Select(row => ToConnection(row, accountId))
        .ToList();

    public async Task<Connection?> FindAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken) =>
        await db.Connections.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == connectionId && (row.PairLow == accountId || row.PairHigh == accountId), cancellationToken)
            is { } found
            ? ToConnection(found, accountId)
            : null;

    private static Connection ToConnection(ConnectionRow row, Guid accountId) => new(
        row.Id, row.PairLow == accountId ? row.PairHigh : row.PairLow, InviteNames.ParseConnectionState(row.State), row.CreatedAt);
}
