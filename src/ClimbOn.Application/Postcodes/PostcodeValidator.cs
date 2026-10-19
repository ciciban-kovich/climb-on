using ClimbOn.Domain.Postcodes;

namespace ClimbOn.Application.Postcodes;

// S15, S102.2: a postcode a profile may be created with or changed to.
public sealed class PostcodeValidator(IPostcodeStore store)
{
    // Returns the postcode when it is accepted; throws DomainError otherwise.
    public async Task<string> ValidateAsync(string? postcode, CancellationToken cancellationToken)
    {
        PostcodeRules.EnsureWellFormed(postcode);
        return PostcodeRules.Accept(await store.FindAsync(postcode, cancellationToken));
    }
}
