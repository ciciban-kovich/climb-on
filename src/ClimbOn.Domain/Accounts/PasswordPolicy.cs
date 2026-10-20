using System.Diagnostics.CodeAnalysis;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Accounts;

// S79: 10 to 128 characters, no composition rules. A character is a Unicode code point, so a
// character outside the Basic Multilingual Plane counts once, not as its two UTF-16 units.
public static class PasswordPolicy
{
    public const string TooShortCode = "password_too_short";
    public const string TooLongCode = "password_too_long";

    public const int MinimumLength = 10;
    public const int MaximumLength = 128;

    public static void Ensure([NotNull] string? password)
    {
        var length = password?.EnumerateRunes().Count() ?? 0;
        if (password is null || length < MinimumLength)
        {
            throw new DomainError(TooShortCode);
        }

        if (length > MaximumLength)
        {
            throw new DomainError(TooLongCode);
        }
    }
}
