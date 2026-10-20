namespace ClimbOn.Infrastructure.Persistence.Accounts;

// A row of accounts.
public sealed class AccountRow
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public required string PasswordHash { get; init; }

    public required DateOnly DateOfBirth { get; init; }

    // A language tag, en or nb (S103, S104).
    public required string Language { get; set; }

    public required DateTimeOffset RegisteredAt { get; init; }

    // Null until the address is confirmed (S49).
    public DateTimeOffset? EmailConfirmedAt { get; init; }
}
