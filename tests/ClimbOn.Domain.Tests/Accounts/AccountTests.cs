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

    // Only addresses the email adapter can send to: an ASCII dot-atom before the @, so no double dots,
    // RFC 5322 specials or non-ASCII letters there; internationalised domains are allowed.
    [Theory]
    [Trait("SpecPartial", "S47")]
    [InlineData("ola..nordmann@gmail.com")]
    [InlineData(".ola@example.no")]
    [InlineData("ola.@example.no")]
    [InlineData("ola@gmail..com")]
    [InlineData("a,b@example.test")]
    [InlineData("<a>@x.test")]
    [InlineData("a(b)@x.test")]
    [InlineData("a;b@x.test")]
    [InlineData("a:b@x.test")]
    [InlineData("a[b]@x.test")]
    [InlineData("\"a b\"@x.test")]
    [InlineData("øystein@example.no")]
    [InlineData("ola@-example.no")]
    [InlineData("ola@example-.no")]
    [InlineData("ola@x_y.no")]
    [InlineData("ola@[192.0.2.1]")]
    public void Undeliverable_email_is_refused(string email)
    {
        var error = Assert.Throws<DomainError>(() => Account.Register(Guid.NewGuid(), Valid with { Email = email }, Current, Now, password => password));

        Assert.Equal(Account.EmailInvalidCode, error.Code);
    }

    [Theory]
    [Trait("SpecPartial", "S47")]
    [InlineData("ola+klatring@example.no")]
    [InlineData("ola.nordmann@gmail.com")]
    [InlineData("o'brien@example.ie")]
    [InlineData("ola@ærø.no")]
    [InlineData("ola@sub.xn--r-3fa9c.no")]
    [InlineData("ola@my-gym.example")]
    public void Deliverable_email_is_accepted(string email) =>
        Assert.Equal(email, Account.Register(Guid.NewGuid(), Valid with { Email = email }, Current, Now, password => password).Email);

    [Fact]
    [Trait("SpecPartial", "S84")]
    public void Registration_stores_the_normal_form_beside_the_address()
    {
        var account = Account.Register(Guid.NewGuid(), Valid with { Email = " Ola.Nordmann@Ærø.NO " }, Current, Now, password => password);

        Assert.Equal("Ola.Nordmann@Ærø.NO", account.Email);
        Assert.Equal("ola.nordmann@xn--r-3fa9c.no", account.NormalisedEmail);
    }

    // Case, surrounding space, full-width letters and the punycoded spelling of a domain are one address.
    [Theory]
    [Trait("SpecPartial", "S84")]
    [InlineData("ola@example.no", "ola@example.no")]
    [InlineData("  OLA@Example.NO\t", "ola@example.no")]
    [InlineData("ola@ｅｘａｍｐｌｅ.no", "ola@example.no")]
    [InlineData("ola@ＥＸＡＭＰＬＥ.no", "ola@example.no")]
    [InlineData("ola@ærø.no", "ola@xn--r-3fa9c.no")]
    [InlineData("ola@ÆRØ.no", "ola@xn--r-3fa9c.no")]
    [InlineData("ola@xn--r-3fa9c.no", "ola@xn--r-3fa9c.no")]
    [InlineData("ola@XN--R-3FA9C.NO", "ola@xn--r-3fa9c.no")]
    public void Spellings_of_one_address_share_a_normal_form(string email, string expected) =>
        Assert.Equal(expected, Account.NormaliseEmail(email));

    // Format characters (Unicode Cf) are not letters, so they never reach the normal form, where IDNA
    // would drop some of them and make a second spelling of a taken address.
    [Theory]
    [Trait("SpecPartial", "S84")]
    [InlineData("ola​@example.no")]
    [InlineData("ola@exam­ple.no")]
    [InlineData("ola@exam‍ple.no")]
    [InlineData("ola@‮example.no")]
    [InlineData("ola@exam﻿ple.no")]
    public void Format_characters_are_refused(string email)
    {
        Assert.Null(Account.NormaliseEmail(email));
        var error = Assert.Throws<DomainError>(() => Account.Register(Guid.NewGuid(), Valid with { Email = email }, Current, Now, password => password));
        Assert.Equal(Account.EmailInvalidCode, error.Code);
    }

    // An address without a normal form is refused: a label IDNA cannot map (over 63 characters, or a
    // letter IDNA maps to a space, which is no host name), or one of 254 characters whose punycoded
    // domain makes it longer (each label of 40 æ becomes 46 characters).
    public static TheoryData<string> WithoutNormalForm => new()
    {
        $"ola@{new string('a', 64)}.no",
        "ola@aͺb.no",
        "ola@ﷺ.no",
        "ola@aﹰb.no",
        $"{new string('o', 254 - 1 - 84)}@{new string('æ', 40)}.{new string('æ', 40)}.no",
    };

    [Theory]
    [Trait("SpecPartial", "S84")]
    [MemberData(nameof(WithoutNormalForm))]
    public void Address_without_a_normal_form_is_refused(string email)
    {
        Assert.InRange(email.Length, 1, Account.MaximumEmailLength);
        var error = Assert.Throws<DomainError>(() => Account.Register(Guid.NewGuid(), Valid with { Email = email }, Current, Now, password => password));
        Assert.Equal(Account.EmailInvalidCode, error.Code);
    }

    // The edge the second case above crosses: with a local part 12 characters shorter, the punycoded
    // form is exactly 254 characters, and accepted.
    [Fact]
    [Trait("SpecPartial", "S84")]
    public void Punycoded_address_of_254_characters_is_accepted() =>
        Assert.Equal(254, Account.NormaliseEmail($"{new string('o', 254 - 1 - 96)}@{new string('æ', 40)}.{new string('æ', 40)}.no")?.Length);

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
