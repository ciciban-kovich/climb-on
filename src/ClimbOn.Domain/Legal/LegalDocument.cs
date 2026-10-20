namespace ClimbOn.Domain.Legal;

// S111, S112: the two documents a climber accepts or acknowledges.
public enum LegalDocument
{
    Terms,
    Privacy,
}

public static class LegalDocuments
{
    // The document's name in URLs and storage.
    public static string Name(this LegalDocument document) => document switch
    {
        LegalDocument.Terms => "terms",
        LegalDocument.Privacy => "privacy",
        _ => throw new ArgumentOutOfRangeException(nameof(document), document, null),
    };

    public static LegalDocument? FromName(string? name) => name switch
    {
        "terms" => LegalDocument.Terms,
        "privacy" => LegalDocument.Privacy,
        _ => null,
    };
}

// The current version of each document.
public sealed record LegalVersions(int Terms, int Privacy);

// S111: the version of a document the climber accepted (terms) or acknowledged (privacy), and when.
public sealed record LegalAcknowledgement(LegalDocument Document, int Version, DateTimeOffset At);
