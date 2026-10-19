using System.Diagnostics.CodeAnalysis;
using ClimbOn.Domain.Errors;

namespace ClimbOn.Domain.Postcodes;

// S15, S102.2: a postcode a profile may be created with or changed to.
public static class PostcodeRules
{
    public const string MalformedCode = "postcode_malformed";
    public const string UnknownCode = "postcode_unknown";
    public const string RetiredCode = "postcode_retired";

    public static IReadOnlyList<string> Codes { get; } = [MalformedCode, UnknownCode, RetiredCode];

    // Four ASCII digits.
    public static bool IsWellFormed([NotNullWhen(true)] string? postcode) =>
        postcode is { Length: 4 } && postcode.All(char.IsAsciiDigit);

    public static void EnsureWellFormed([NotNull] string? postcode)
    {
        if (!IsWellFormed(postcode))
        {
            throw new DomainError(MalformedCode);
        }
    }

    // stored is the table's row for the postcode, null when it has none. Returns the postcode when it
    // is accepted; throws DomainError otherwise.
    public static string Accept(StoredPostcode? stored)
    {
        if (stored is null)
        {
            throw new DomainError(UnknownCode);
        }

        if (stored.Retired)
        {
            throw new DomainError(RetiredCode);
        }

        return stored.Code;
    }
}
