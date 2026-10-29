namespace ClimbOn.Domain.Accounts;

// Specs glossary: an account is dormant when its last activity is more than 60 days ago (S107).
public static class DormancyPolicy
{
    public static readonly TimeSpan After = TimeSpan.FromDays(60);

    // An account is not dormant at now if its last activity is at or after this time.
    public static DateTimeOffset ActiveIfLastActiveAtOrAfter(DateTimeOffset now) => now - After;
}
