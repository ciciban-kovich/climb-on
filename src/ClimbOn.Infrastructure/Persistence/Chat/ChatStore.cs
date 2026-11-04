using ClimbOn.Application.Chat;
using ClimbOn.Domain.Chat;
using ClimbOn.Infrastructure.Persistence.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Chat;

public sealed class ChatStore(ClimbOnDbContext db) : IChatStore
{
    public async Task<IReadOnlyList<Connection>> ListConnectionsAsync(Guid accountId, CancellationToken cancellationToken) =>
        (await db.Connections.AsNoTracking()
            .Where(row => row.PairLow == accountId || row.PairHigh == accountId)
            .ToListAsync(cancellationToken))
        .Select(row => ToConnection(row, accountId))
        .ToList();

    public async Task<Connection?> FindConnectionAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken) =>
        await db.Connections.AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == connectionId && (row.PairLow == accountId || row.PairHigh == accountId), cancellationToken)
            is { } found
            ? ToConnection(found, accountId)
            : null;

    public async Task<IReadOnlyList<Message>> ListMessagesAsync(Guid connectionId, CancellationToken cancellationToken) =>
        (await db.Messages.AsNoTracking()
            .Where(row => row.ConnectionId == connectionId)
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken))
        .Select(row => row.ToMessage())
        .ToList();

    public async Task<bool> AddIfActiveAsync(Message message, CancellationToken cancellationToken)
    {
        // READ COMMITTED: after waiting on a change of the row, the lock reads the row as that change committed it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var active = await db.Database.SqlQuery<Guid>(
                $"""SELECT id AS "Value" FROM connections WHERE id = {message.ConnectionId} AND state = {InviteNames.ConnectionActive} FOR SHARE""")
            .ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return false;
        }

        db.Messages.Add(new MessageRow
        {
            Id = message.Id,
            ConnectionId = message.ConnectionId,
            SenderId = message.SenderId,
            Text = message.Text,
            SentAt = message.SentAt,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static Connection ToConnection(ConnectionRow row, Guid accountId) => new(
        row.Id,
        row.PairLow == accountId ? row.PairHigh : row.PairLow,
        row.State switch
        {
            InviteNames.ConnectionActive => ConnectionState.Active,
            InviteNames.ConnectionPaused => ConnectionState.Paused,
            InviteNames.ConnectionEnded => ConnectionState.Ended,
            _ => throw new InvalidOperationException("Unknown connection state."),
        },
        row.CreatedAt);
}
