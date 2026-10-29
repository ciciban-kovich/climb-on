using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Legal;
using ClimbOn.Infrastructure.Accounts;
using ClimbOn.Infrastructure.Persistence.Accounts;
using ClimbOn.Infrastructure.Tests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClimbOn.Infrastructure.Tests.Accounts;

// What registration stores (S27, S104, S111, C42). The registration tests see only the API's
// answers, which come from the domain object, so they cannot see the row.
public sealed class AccountStoreTests(MigratedDatabase database) : IClassFixture<MigratedDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    // Each language, and terms and privacy at different versions, so neither can be stored as a constant.
    [Theory]
    [InlineData("nb")]
    [InlineData("en")]
    public async Task Registration_stores_the_account_and_its_acknowledgements(string language)
    {
        const string password = "Ikke-lagret-i-klartekst";
        var email = $"climber-{language}@example.test";
        var dateOfBirth = new DateOnly(1990, 5, 17);
        await PublishTermsVersion2Async();
        var account = Account.Register(
            Guid.NewGuid(),
            new EmailRegistration(email, password, dateOfBirth, language, TermsVersion: 2, PrivacyVersion: 1),
            new LegalVersions(Terms: 2, Privacy: 1),
            Now,
            new IdentityPasswordHasher().Hash);

        await using (var db = database.CreateContext())
        {
            await new AccountStore(db).AddAsync(account, TestContext.Current.CancellationToken);
        }

        await using var read = database.CreateContext();
        var row = await read.Accounts.SingleAsync(row => row.Id == account.Id, TestContext.Current.CancellationToken);
        Assert.Equal(email, row.Email);
        Assert.Equal(dateOfBirth, row.DateOfBirth);
        Assert.Equal(language, row.Language);
        Assert.Equal(Now, row.RegisteredAt);

        Assert.NotEqual(password, row.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<IdentityPasswordHasher>().VerifyHashedPassword(null!, row.PasswordHash, password));

        var acknowledgements = await read.LegalAcknowledgements
            .Where(acknowledgement => acknowledgement.AccountId == account.Id)
            .OrderBy(acknowledgement => acknowledgement.Document)
            .Select(acknowledgement => new { acknowledgement.Document, acknowledgement.Version, acknowledgement.AcknowledgedAt })
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [new { Document = "privacy", Version = 1, AcknowledgedAt = Now }, new { Document = "terms", Version = 2, AcknowledgedAt = Now }],
            acknowledgements);
    }

    // The migration seeds version 1 of each; an acknowledgement must name a published version.
    private async Task PublishTermsVersion2Async()
    {
        await using var db = database.CreateContext();
        if (await db.LegalDocuments.AnyAsync(row => row.Document == "terms" && row.Version == 2, TestContext.Current.CancellationToken))
        {
            return;
        }

        db.LegalDocuments.Add(new LegalDocumentRow
        {
            Document = "terms",
            Version = 2,
            PublishedAt = Now.AddDays(-1),
            TextEnglish = "Terms, version 2.",
            TextBokmal = "Vilkår, versjon 2.",
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
