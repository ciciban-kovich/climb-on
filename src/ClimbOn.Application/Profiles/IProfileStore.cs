using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Profiles;

// A gym (S17) as a profile or a card refers to it.
public sealed record Gym(Guid Id, string Name, bool Active);

public interface IProfileStore
{
    // The account's profile; null when it has none yet.
    Task<Profile?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    // Creates the account's profile or replaces all of it, in one transaction.
    Task SaveAsync(Guid accountId, Profile profile, CancellationToken cancellationToken);

    // Those of these ids that are gyms.
    Task<IReadOnlyDictionary<Guid, Gym>> FindGymsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
