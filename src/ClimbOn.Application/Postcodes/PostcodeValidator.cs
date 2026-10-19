using ClimbOn.Domain.Errors;

namespace ClimbOn.Application.Postcodes;

// S15, S102.2: a postcode a profile may be created with or changed to.
public sealed class PostcodeValidator(IPostcodeStore store)
{
    public const string MalformedCode = "postcode_malformed";
    public const string UnknownCode = "postcode_unknown";
    public const string RetiredCode = "postcode_retired";

    public static IReadOnlyList<string> Codes { get; } = [MalformedCode, UnknownCode, RetiredCode];

    // Returns the postcode when it is accepted; throws DomainError otherwise.
    public async Task<string> ValidateAsync(string? postcode, CancellationToken cancellationToken)
    {
        if (postcode is not { Length: 4 } || !postcode.All(char.IsAsciiDigit))
        {
            throw new DomainError(MalformedCode);
        }

        var stored = await store.FindAsync(postcode, cancellationToken) ?? throw new DomainError(UnknownCode);
        if (stored.Retired)
        {
            throw new DomainError(RetiredCode);
        }

        return stored.Code;
    }
}
