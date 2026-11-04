using ClimbOn.Application.Abstractions;
using ClimbOn.Domain.Chat;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Application.Chat;

// Sending and reading the messages of a connection (S9, S115). Only its two climbers can do either; to anyone else
// the connection does not exist.
public sealed class Messages(IChatStore chat, IClock clock)
{
    public const string ConnectionNotFoundCode = "connection_not_found";
    public const string ConnectionNotActiveCode = "connection_not_active";

    public static IReadOnlyList<string> Codes { get; } = [ConnectionNotFoundCode, ConnectionNotActiveCode, .. Message.Codes];

    // S9: only within an active connection.
    public async Task<Message> SendAsync(Guid senderId, Guid connectionId, string? text, CancellationToken cancellationToken)
    {
        var message = Message.Write(Guid.NewGuid(), connectionId, senderId, text, clock.UtcNow);
        await FindAsync(senderId, connectionId, cancellationToken);
        if (!await chat.AddIfActiveAsync(message, cancellationToken))
        {
            throw new DomainError(ConnectionNotActiveCode, DomainErrorKind.Conflict);
        }

        return message;
    }

    // S9: readable in every state of the connection.
    public async Task<IReadOnlyList<Message>> ListAsync(Guid viewerId, Guid connectionId, CancellationToken cancellationToken)
    {
        await FindAsync(viewerId, connectionId, cancellationToken);
        return await chat.ListMessagesAsync(connectionId, cancellationToken);
    }

    private async Task FindAsync(Guid accountId, Guid connectionId, CancellationToken cancellationToken)
    {
        if (await chat.FindConnectionAsync(accountId, connectionId, cancellationToken) is null)
        {
            throw new DomainError(ConnectionNotFoundCode, DomainErrorKind.NotFound);
        }
    }
}
