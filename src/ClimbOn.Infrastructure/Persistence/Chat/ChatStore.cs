using ClimbOn.Application.Chat;
using ClimbOn.Domain.Chat;
using ClimbOn.Infrastructure.Persistence.Invites;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Chat;

public sealed class ChatStore(ClimbOnDbContext db) : IChatStore
{
    // Keyset on Position, the stored order, which is unique and never changes: walking back page by page gives every
    // message once, also those sent at one instant.
    public async Task<MessagePage?> ListMessagesAsync(
        Guid connectionId, Guid? before, int size, CancellationToken cancellationToken)
    {
        var rows = db.Messages.AsNoTracking().Where(row => row.ConnectionId == connectionId);
        if (before is { } id)
        {
            var cursor = await rows.Where(row => row.Id == id).Select(row => (long?)row.Position).SingleOrDefaultAsync(cancellationToken);
            if (cursor is not { } position)
            {
                return null;
            }

            rows = rows.Where(row => row.Position < position);
        }

        // One more than the page shows whether older messages exist.
        var newest = await rows.OrderByDescending(row => row.Position).Take(size + 1).ToListAsync(cancellationToken);
        return new MessagePage(
            newest.Take(size).Reverse().Select(row => row.ToMessage()).ToList(),
            newest.Count > size);
    }

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
