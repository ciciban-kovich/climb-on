using ClimbOn.Domain.Accounts;

namespace ClimbOn.Application.Abstractions;

// The subject, text and links of each email, in the recipient's language (S105). The api owns the texts.
public interface IEmailContent
{
    EmailMessage EmailConfirmation(string to, Language language, string token);

    // S87: someone tried to register with the address of a password account.
    EmailMessage RegistrationAttempt(string to, Language language);
}
