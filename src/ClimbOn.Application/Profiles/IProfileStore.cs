using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Profiles;

// A gym (S17) as a profile or a card refers to it.
public sealed record Gym(Guid Id, string Name, bool Active);

// Parts of a stored profile as they were saved, read without today's rules, so that one those rules refuse can still
// be named (S35) or replaced (S3).
public sealed record StoredProfile(string DisplayName, string Postcode, IReadOnlyList<Guid> HomeGyms);

public interface IProfileStore
{
    // The account's profile; null when it has none yet. Throws DomainError when today's rules refuse it.
    Task<Profile?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    // Those of these accounts that have a profile, as stored.
    Task<IReadOnlyDictionary<Guid, StoredProfile>> FindStoredAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    // Creates the account's profile or replaces all of it, in one transaction.
    Task SaveAsync(Guid accountId, Profile profile, CancellationToken cancellationToken);

    // Those of these ids that are gyms.
    Task<IReadOnlyDictionary<Guid, Gym>> FindGymsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
