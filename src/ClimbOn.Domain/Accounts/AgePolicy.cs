using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Accounts;

// S28: registration is rejected if the climber is under 18 on the registration date (a UTC date, C3).
public static class AgePolicy
{
    public const string UnderageCode = "under_18";

    public const int MinimumAge = 18;

    // Age in completed years on today. Someone born on 29 February completes a year on 1 March in a
    // year without that date.
    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        return (today.Month, today.Day).CompareTo((dateOfBirth.Month, dateOfBirth.Day)) < 0 ? age - 1 : age;
    }

    public static void EnsureAdult(DateOnly dateOfBirth, DateOnly today)
    {
        if (AgeOn(dateOfBirth, today) < MinimumAge)
        {
            throw new DomainError(UnderageCode);
        }
    }
}
