using ClimbOn.Application.Chat;
using ClimbOn.Domain.Chat;
using ClimbOn.Infrastructure.Persistence.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Chat;

public sealed class ChatStore(ClimbOnDbContext db) : IChatStore
{
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
        if (!await ConnectionLocks.ShareIfActiveAsync(db, message.ConnectionId, cancellationToken))
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
}
