using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// What a password login is checked against.
public sealed record AccountCredentials(Guid Id, string PasswordHash);

// The account an address belongs to (S84), for the notice S87 sends to it.
public sealed record EmailOwner(Guid Id, string Email, Language Language);

// The account as its own climber sees it.
public sealed record OwnAccount(string Email, Language Language, bool EmailConfirmed);

public interface IAccountStore
{
    // Stores the account with its legal acknowledgements, and the link tokens and outbox rows the
    // request has added, in one transaction. False when the address already belongs to an account
    // (S84): then nothing is stored, and nothing the request added is left pending.
    Task<bool> AddAsync(Account account, CancellationToken cancellationToken);

    // Marks the unused email-confirmation token with this hash used and its account's address
    // confirmed, atomically. False when there is no such token.
    Task<bool> ConfirmEmailAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken);

    // The account whose address has this normal form (Account.NormaliseEmail); null when there is none.
    Task<AccountCredentials?> FindCredentialsAsync(string normalisedEmail, CancellationToken cancellationToken);

    // The same lookup, for the owner's address and language.
    Task<EmailOwner?> FindEmailOwnerAsync(string normalisedEmail, CancellationToken cancellationToken);

    Task<OwnAccount?> FindOwnAsync(Guid id, CancellationToken cancellationToken);

    Task SetLanguageAsync(Guid id, Language language, CancellationToken cancellationToken);
}
