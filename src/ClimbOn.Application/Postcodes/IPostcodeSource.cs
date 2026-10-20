using ClimbOn.Domain.Postcodes;

namespace ClimbOn.Application.Postcodes;

public interface IPostcodeSource
{
    // Every postcode that has an area in the current dataset version (S100), each code once.
    Task<IReadOnlyList<PostcodeArea>> LoadAsync(CancellationToken cancellationToken);
}
