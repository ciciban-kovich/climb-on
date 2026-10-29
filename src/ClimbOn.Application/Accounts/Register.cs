using ClimbOn.Application.Abstractions;
using ClimbOn.Application.Legal;
using ClimbOn.Application.Outbox;
using ClimbOn.Application.Tokens;
using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Accounts;

// S1 (email), S27, S28, S47, S49, S79, S84, S87, S104, S111: registration with email and password.
public sealed class Register(
    IAccountStore accounts,
    ILegalDocumentStore legalDocuments,
    IPasswordHasher hasher,
    ILinkTokenStore linkTokens,
    EmailOutbox outbox,
    IEmailContent emails,
    IClock clock)
{
    // Returns the language stored for the new account (S104); throws DomainError when a rule refuses it.
    // A taken address (S87) returns the language the account would have had, so the answer is the same.
    public async Task<Language> RunAsync(EmailRegistration registration, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var current = await legalDocuments.CurrentVersionsAsync(cancellationToken);
        // Random, so the ID a card carries reveals nothing about the climber (S51).
        var account = Account.Register(Guid.NewGuid(), registration, current, now, hasher.Hash);

        // S49: the confirmation link, committed with the account.
        var token = LinkTokens.Create();
        linkTokens.Add(new LinkToken(LinkTokens.Hash(token), account.Id, LinkPurpose.EmailConfirmation, now));
        var email = outbox.Enqueue(account.Id, emails.EmailConfirmation(account.Email, account.Language, token));
        if (!await accounts.AddAsync(account, cancellationToken))
        {
            // S84: the unique index decided, so parallel registrations cannot both pass. Nothing
            // removes accounts until erasure (M2), so the owner the insert collided with is still there.
            var owner = await accounts.FindEmailOwnerAsync(account.NormalisedEmail, cancellationToken)
                ?? throw new InvalidOperationException("The account that holds the address is gone.");
            email = await outbox.EnqueueAsync(owner.Id, emails.RegistrationAttempt(owner.Email, owner.Language), cancellationToken);
        }

        await outbox.DeliverAsync(email, cancellationToken);
        return account.Language;
    }
}
