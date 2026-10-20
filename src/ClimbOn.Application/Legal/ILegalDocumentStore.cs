using ClimbOn.Domain.Legal;

namespace ClimbOn.Application.Legal;

// A published version of a document, in both supported languages (S103).
public sealed record PublishedLegalDocument(
    LegalDocument Document, int Version, DateTimeOffset PublishedAt, string TextEnglish, string TextBokmal);

public interface ILegalDocumentStore
{
    // The highest published version of each document.
    Task<LegalVersions> CurrentVersionsAsync(CancellationToken cancellationToken);

    // The given version, or the current one when version is null; null when there is no such version.
    Task<PublishedLegalDocument?> FindAsync(LegalDocument document, int? version, CancellationToken cancellationToken);
}
