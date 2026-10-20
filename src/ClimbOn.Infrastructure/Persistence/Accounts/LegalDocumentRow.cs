namespace ClimbOn.Infrastructure.Persistence.Accounts;

// A row of legal_documents: one published version of the terms or the privacy policy (S112).
public sealed class LegalDocumentRow
{
    // terms or privacy.
    public required string Document { get; init; }

    public required int Version { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required string TextEnglish { get; init; }

    public required string TextBokmal { get; init; }
}
