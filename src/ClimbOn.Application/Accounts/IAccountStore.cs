using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

public interface IAccountStore
{
    // Stores the account with its legal acknowledgements, and the link tokens and outbox rows the
    // request has added, in one transaction.
    Task AddAsync(Account account, CancellationToken cancellationToken);

    // Marks the unused email-confirmation token with this hash used and its account's address
    // confirmed, atomically. False when there is no such token.
    Task<bool> ConfirmEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);
}
