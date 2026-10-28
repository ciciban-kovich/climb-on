using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Profiles;

public interface IProfileStore
{
    // The account's profile; null when it has none yet.
    Task<Profile?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    // Creates the account's profile or replaces all of it, in one transaction.
    Task SaveAsync(Guid accountId, Profile profile, CancellationToken cancellationToken);

    // Whether each of these gyms is active, for the ids that are gyms.
    Task<IReadOnlyDictionary<Guid, bool>> FindGymsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
