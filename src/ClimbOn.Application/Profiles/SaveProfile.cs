using ClimbOn.Application.Postcodes;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Profiles;

namespace ClimbOn.Application.Profiles;

// S3: a climber creates their profile or replaces it. Home gyms left out of a replacement keep the stored list;
// an explicit empty list clears it.
public sealed class SaveProfile(IProfileStore profiles, PostcodeValidator postcodes)
{
    public const string GymUnknownCode = "gym_unknown";
    public const string GymInactiveCode = "gym_inactive";

    public static IReadOnlyList<string> Codes { get; } = [GymUnknownCode, GymInactiveCode];

    // Returns the profile as stored; throws DomainError when a rule refuses it.
    public async Task<Profile> RunAsync(Guid accountId, ProfileDraft draft, CancellationToken cancellationToken)
    {
        // As stored, so that a profile today's rules refuse can still be replaced.
        var stored = (await profiles.FindStoredAsync([accountId], cancellationToken)).GetValueOrDefault(accountId);
        // Home gyms left out or null keep the stored list, inactive ones included (S21), so a replacement for a stored
        // profile the climber cannot read back does not lose them. An empty list clears it.
        var profile = Profile.Create(draft.HomeGyms is null && stored is not null ? draft with { HomeGyms = stored.HomeGyms } : draft);

        // S15, S102.2: checked against the table only on create or change, so an unchanged
        // retired postcode is kept (S102.3).
        if (stored?.Postcode != profile.Postcode)
        {
            await postcodes.ValidateAsync(profile.Postcode, cancellationToken);
        }

        // S18 for gyms the profile did not list before; one it listed stays even once inactive (S21).
        var added = profile.HomeGyms.Except(stored?.HomeGyms ?? []).ToList();
        var gyms = await profiles.FindGymsAsync(added, cancellationToken);
        foreach (var gym in added)
        {
            if (!gyms.TryGetValue(gym, out var found))
            {
                throw new DomainError(GymUnknownCode);
            }

            if (!found.Active)
            {
                throw new DomainError(GymInactiveCode);
            }
        }

        await profiles.SaveAsync(accountId, profile, cancellationToken);
        return profile;
    }
}
