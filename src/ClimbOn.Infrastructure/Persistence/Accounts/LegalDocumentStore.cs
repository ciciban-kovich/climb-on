using ClimbOn.Application.Legal;
using ClimbOn.Domain.Legal;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Accounts;

public sealed class LegalDocumentStore(ClimbOnDbContext db) : ILegalDocumentStore
{
    public async Task<LegalVersions> CurrentVersionsAsync(CancellationToken cancellationToken)
    {
        var current = await db.LegalDocuments.AsNoTracking()
            .GroupBy(row => row.Document)
            .Select(group => new { Document = group.Key, Version = group.Max(row => row.Version) })
            .ToDictionaryAsync(row => row.Document, row => row.Version, StringComparer.Ordinal, cancellationToken);
        return new LegalVersions(Current(LegalDocument.Terms), Current(LegalDocument.Privacy));

        // The migration publishes version 1 of each, so a missing document is a broken database.
        int Current(LegalDocument document) => current.TryGetValue(document.Name(), out var version)
            ? version
            : throw new InvalidOperationException($"No published version of {document.Name()}.");
    }

    public async Task<PublishedLegalDocument?> FindAsync(LegalDocument document, int? version, CancellationToken cancellationToken)
    {
        var name = document.Name();
        var versions = db.LegalDocuments.AsNoTracking().Where(row => row.Document == name);
        var row = version is { } wanted
            ? await versions.SingleOrDefaultAsync(row => row.Version == wanted, cancellationToken)
            : await versions.OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new PublishedLegalDocument(document, row.Version, row.PublishedAt, row.TextEnglish, row.TextBokmal);
    }
}
