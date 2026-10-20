using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Legal;

namespace ClimbOn.Domain.Accounts;

// What a climber sends to register with email and password (S1). Every field may be missing.
public sealed record EmailRegistration(
    string? Email,
    string? Password,
    DateOnly? DateOfBirth,
    string? LanguageSetting,
    int? TermsVersion,
    int? PrivacyVersion);

public sealed class Account
{
    public const string EmailInvalidCode = "email_invalid";
    public const string DateOfBirthRequiredCode = "date_of_birth_required";
    public const string TermsNotAcceptedCode = "terms_not_accepted";
    public const string PrivacyNotAcknowledgedCode = "privacy_not_acknowledged";

    public const int MaximumEmailLength = 254;

    // The characters besides ASCII letters and digits an atom of the part before the @ may hold (RFC 5321).
    private const string AtomSpecials = "!#$%&'*+-/=?^_`{|}~";

    private Account(
        Guid id,
        string email,
        string passwordHash,
        DateOnly dateOfBirth,
        Language language,
        DateTimeOffset registeredAt,
        IReadOnlyList<LegalAcknowledgement> acknowledgements)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        DateOfBirth = dateOfBirth;
        Language = language;
        RegisteredAt = registeredAt;
        Acknowledgements = acknowledgements;
    }

    public static IReadOnlyList<string> RegistrationCodes { get; } =
    [
        EmailInvalidCode, PasswordPolicy.TooShortCode, PasswordPolicy.TooLongCode, DateOfBirthRequiredCode,
        AgePolicy.UnderageCode, TermsNotAcceptedCode, PrivacyNotAcknowledgedCode,
    ];

    public Guid Id { get; }

    // S47: exactly one.
    public string Email { get; }

    public string PasswordHash { get; }

    // S27: a date only.
    public DateOnly DateOfBirth { get; }

    public Language Language { get; }

    public DateTimeOffset RegisteredAt { get; }

    public IReadOnlyList<LegalAcknowledgement> Acknowledgements { get; }

    // S27, S28, S47, S79, S104, S111. current holds the documents' current versions at now.
    // hashPassword runs only once every rule has passed, so a refused request costs no hash.
    public static Account Register(
        Guid id, EmailRegistration registration, LegalVersions current, DateTimeOffset now, Func<string, string> hashPassword)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(hashPassword);

        var email = EnsureEmail(registration.Email);
        PasswordPolicy.Ensure(registration.Password);
        if (registration.DateOfBirth is not { } dateOfBirth)
        {
            throw new DomainError(DateOfBirthRequiredCode);
        }

        AgePolicy.EnsureAdult(dateOfBirth, DateOnly.FromDateTime(now.UtcDateTime));
        if (registration.TermsVersion != current.Terms)
        {
            throw new DomainError(TermsNotAcceptedCode);
        }

        if (registration.PrivacyVersion != current.Privacy)
        {
            throw new DomainError(PrivacyNotAcknowledgedCode);
        }

        return new Account(
            id,
            email,
            hashPassword(registration.Password),
            dateOfBirth,
            Languages.FromClientSetting(registration.LanguageSetting),
            now,
            [
                new LegalAcknowledgement(LegalDocument.Terms, current.Terms, now),
                new LegalAcknowledgement(LegalDocument.Privacy, current.Privacy, now),
            ]);
    }

    // Surrounding white space is dropped. What remains must be an address the email adapter can send to:
    // an ASCII dot-atom before the one @ (RFC 5321: atoms of letters, digits and the specials below, joined
    // by single dots), and after it at least two dot-separated labels of letters, digits and inner hyphens.
    // Labels may hold non-ASCII letters (internationalised domains are sent punycoded); the part before
    // the @ may not, because the provider is not known to accept SMTPUTF8.
    private static string EnsureEmail(string? value)
    {
        var email = value?.Trim();
        var at = email?.IndexOf('@', StringComparison.Ordinal) ?? -1;
        if (email is null
            || email.Length > MaximumEmailLength
            || at <= 0
            || !email[..at].Split('.').All(IsAtom)
            || email[(at + 1)..].Split('.') is not { Length: >= 2 } labels
            || !labels.All(IsDomainLabel))
        {
            throw new DomainError(EmailInvalidCode);
        }

        return email;
    }

    private static bool IsAtom(string atom) =>
        atom.Length > 0 && atom.All(character => char.IsAsciiLetterOrDigit(character) || AtomSpecials.Contains(character));

    private static bool IsDomainLabel(string label) =>
        label.Length > 0
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(character => char.IsLetterOrDigit(character) || character == '-');

}
