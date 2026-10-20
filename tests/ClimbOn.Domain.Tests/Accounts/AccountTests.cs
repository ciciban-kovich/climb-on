using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Legal;
using Xunit;

namespace ClimbOn.Domain.Tests.Accounts;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly LegalVersions Current = new(Terms: 3, Privacy: 2);

    private static readonly EmailRegistration Valid =
        new(" climber@example.test ", "correct horse battery", new DateOnly(1990, 5, 17), "nn-NO", TermsVersion: 3, PrivacyVersion: 2);

    public static TheoryData<EmailRegistration, string> Refused => new()
    {
        { Valid with { Email = "climber" }, Account.EmailInvalidCode },
        { Valid with { Password = "short" }, PasswordPolicy.TooShortCode },
        { Valid with { DateOfBirth = null }, Account.DateOfBirthRequiredCode },
        { Valid with { DateOfBirth = new DateOnly(2008, 1, 16) }, AgePolicy.UnderageCode },
        { Valid with { TermsVersion = null }, Account.TermsNotAcceptedCode },
        { Valid with { TermsVersion = 2 }, Account.TermsNotAcceptedCode },
        { Valid with { PrivacyVersion = null }, Account.PrivacyNotAcknowledgedCode },
        { Valid with { PrivacyVersion = 3 }, Account.PrivacyNotAcknowledgedCode },
    };

    [Fact]
    [Trait("SpecPartial", "S27")]
    [Trait("SpecPartial", "S47")]
    [Trait("SpecPartial", "S104")]
    [Trait("SpecPartial", "S111")]
    public void Registration_stores_its_values()
    {
        var id = Guid.NewGuid();

        var account = Account.Register(id, Valid, Current, Now, password => $"hash:{password}");

        Assert.Equal(id, account.Id);
        Assert.Equal("climber@example.test", account.Email);
        Assert.Equal("hash:correct horse battery", account.PasswordHash);
        Assert.Equal(new DateOnly(1990, 5, 17), account.DateOfBirth);
        Assert.Equal(Language.Bokmal, account.Language);
        Assert.Equal(Now, account.RegisteredAt);
        Assert.Equal(
            new[] { new LegalAcknowledgement(LegalDocument.Terms, 3, Now), new LegalAcknowledgement(LegalDocument.Privacy, 2, Now) },
            account.Acknowledgements);
    }

    [Theory]
    [Trait("SpecPartial", "S111")]
    [MemberData(nameof(Refused))]
    public void Refused_registration_is_never_hashed(EmailRegistration registration, string code)
    {
        var hashed = false;

        var error = Assert.Throws<DomainError>(() => Account.Register(Guid.NewGuid(), registration, Current, Now, password =>
        {
            hashed = true;
            return password;
        }));

        Assert.Equal(code, error.Code);
        Assert.False(hashed);
    }

    // The registration date is the UTC date of now (C3): 00:30 at UTC+1 is still the day before in UTC.
    [Fact]
    [Trait("SpecPartial", "S28")]
    public void Age_is_checked_on_the_utc_date()
    {
        var late = new DateTimeOffset(2026, 1, 15, 0, 30, 0, TimeSpan.FromHours(1));
        var registration = Valid with { DateOfBirth = new DateOnly(2008, 1, 15) };

        var error = Assert.Throws<DomainError>(() => Account.Register(Guid.NewGuid(), registration, Current, late, password => password));

        Assert.Equal(AgePolicy.UnderageCode, error.Code);
    }

    [Fact]
    public void Every_registration_code_is_distinct() =>
        Assert.Equal(Account.RegistrationCodes.Count, Account.RegistrationCodes.Distinct(StringComparer.Ordinal).Count());
}
