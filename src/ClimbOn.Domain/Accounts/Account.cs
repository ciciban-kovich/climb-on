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

    // Surrounding white space is dropped. What remains is one @ with text on both sides, a dot in the
    // part after it, and no white space or control characters.
    private static string EnsureEmail(string? value)
    {
        var email = value?.Trim();
        var at = email?.IndexOf('@', StringComparison.Ordinal) ?? -1;
        if (email is null
            || email.Length > MaximumEmailLength
            || at <= 0
            || at != email.LastIndexOf('@')
            || !email.AsSpan(at + 1).Contains('.')
            || email.EndsWith('.')
            || email.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new DomainError(EmailInvalidCode);
        }

        return email;
    }
}
