namespace ClimbOn.Infrastructure.Persistence.Accounts;

// A row of legal_acknowledgements: the version of a document an account accepted or acknowledged, and when (S111).
public sealed class LegalAcknowledgementRow
{
    public required Guid AccountId { get; init; }

    public required string Document { get; init; }

    public required int Version { get; init; }

    public required DateTimeOffset AcknowledgedAt { get; init; }
}
