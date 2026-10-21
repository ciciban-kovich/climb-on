using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// What a password login is checked against.
public sealed record AccountCredentials(Guid Id, string PasswordHash);

// The account as its own climber sees it.
public sealed record OwnAccount(string Email, Language Language, bool EmailConfirmed);

public interface IAccountStore
{
    // Stores the account with its legal acknowledgements, and the link tokens and outbox rows the
    // request has added, in one transaction.
    Task AddAsync(Account account, CancellationToken cancellationToken);

    // Marks the unused email-confirmation token with this hash used and its account's address
    // confirmed, atomically. False when there is no such token.
    Task<bool> ConfirmEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    // The account registered with this address, compared trimmed and case-insensitively; null when there is none.
    Task<AccountCredentials?> FindCredentialsAsync(string email, CancellationToken cancellationToken);

    Task<OwnAccount?> FindOwnAsync(Guid id, CancellationToken cancellationToken);

    Task SetLanguageAsync(Guid id, Language language, CancellationToken cancellationToken);
}
