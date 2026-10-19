using ClimbOn.Domain.Postcodes;

namespace ClimbOn.Application.Postcodes;

// S101: brings the postcode table to the dataset's version, in one transaction (S101.2).
public sealed class PostcodeImport(IPostcodeSource source, IPostcodeStore store)
{
    public async Task<PostcodeImportResult> RunAsync(CancellationToken cancellationToken)
    {
        var dataset = await source.LoadAsync(cancellationToken);
        return await store.ApplyAsync(current => PostcodeDiff.Plan(current, dataset), cancellationToken);
    }
}
