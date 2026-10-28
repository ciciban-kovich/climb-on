using System.Globalization;
using ClimbOn.Api.Climber;

namespace ClimbOn.Integration.Tests.Harness;

public sealed record AccountDraft(string Email, string Password, string DisplayName, DateOnly DateOfBirth);

// Builders for test accounts. Every personal value they produce is registered in FixtureValues.
public sealed class TestData(FixtureValues fixtures)
{
    private static readonly DateOnly FirstBirthday = new(1990, 5, 17);

    private int created;

    public AccountDraft Account(DateOnly? dateOfBirth = null)
    {
        var sequence = Interlocked.Increment(ref created);
        var id = Guid.NewGuid().ToString("N")[..10];
        var account = new AccountDraft(
            $"climber-{id}@example.test",
            $"Pw-{id}-Sikkert!",
            $"Klatrer {id}",
            dateOfBirth ?? FirstBirthday.AddDays(sequence));

        fixtures.Register("email", account.Email);
        fixtures.Register("password", account.Password);
        fixtures.Register("display-name", account.DisplayName);
        // MM/dd/yyyy is how a DateOnly or DateTime renders when logged as a value (invariant culture).
        foreach (var format in new[] { "yyyy-MM-dd", "dd.MM.yyyy", "MM/dd/yyyy" })
        {
            fixtures.Register("date-of-birth", account.DateOfBirth.ToString(format, CultureInfo.InvariantCulture));
        }

        return account;
    }

    // An email address of the given length in characters, for the S47 bound.
    public string Email(int length)
    {
        const string domain = "@example.test";
        var email = new string('a', length - domain.Length) + domain;
        fixtures.Register("email", email);
        return email;
    }

    // Another spelling of an address a test built, for S84.
    public string Email(string spelling)
    {
        fixtures.Register("email", spelling);
        return spelling;
    }

    // A password of the given length in characters, for the S79 bounds.
    public string Password(int length)
    {
        var password = string.Concat(Enumerable.Repeat(Guid.NewGuid().ToString("N"), (length / 32) + 1))[..length];
        fixtures.Register("password", password);
        return password;
    }

    // A postcode a test sends as a climber's home area (S15); see FixturePostcodes.
    public string Postcode(string code)
    {
        fixtures.Register(FixtureValues.PostcodeKind, code);
        return code;
    }

    // A valid profile for the account (S3): its display name, the postcode and the first active fixture gym,
    // indoor bouldering and shared indoor/outdoor lead ranges on the Norwegian scale, one window.
    public ProfileRequest Profile(AccountDraft account, string postcode = "0150") => new(
        account.DisplayName,
        Sex: "female",
        AcceptedSexes: null,
        RopeScale: "norwegian",
        Disciplines:
        [
            new DisciplineEntryRequest("bouldering", ["indoor"], new GradeRangeRequest("5", "6B"), new GradeRangeRequest("4", "7A")),
            new DisciplineEntryRequest("lead", ["indoor", "outdoor"], new GradeRangeRequest("6-", "7"), new GradeRangeRequest("5", "8-")),
        ],
        Certification: "brattkort-led",
        OwnGear: true,
        Postcode(postcode),
        MaxTravelKm: 25,
        HomeGyms: [FixtureGyms.Active[0].Id],
        Windows: [new WindowRequest("tuesday", 18, 21)]);
}
