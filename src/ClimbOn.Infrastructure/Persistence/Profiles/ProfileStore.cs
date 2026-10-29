using System.Data;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Profiles;

public sealed class ProfileStore(ClimbOnDbContext db) : IProfileStore
{
    public async Task<Profile?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var profiles = await ReadAsync(db, [accountId], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return profiles.GetValueOrDefault(accountId);
    }

    // The profiles of those accounts that have one. Reads in the caller's transaction, which must be at least
    // repeatable read: a save between two of these reads could otherwise pair a new rope scale with old grades.
    // A stored profile that today's rules refuse throws, or with invalid is reported to it and left out.
    internal static async Task<Dictionary<Guid, Profile>> ReadAsync(
        ClimbOnDbContext db,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken,
        Action<Guid, DomainError>? invalid = null)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        var profiles = await db.Profiles.AsNoTracking().Where(row => accountIds.Contains(row.AccountId)).ToListAsync(cancellationToken);
        var variants = (await db.DisciplineVariants.AsNoTracking().Where(row => accountIds.Contains(row.AccountId))
            .ToListAsync(cancellationToken)).ToLookup(row => row.AccountId);
        var windows = (await db.AvailabilityWindows.AsNoTracking().Where(row => accountIds.Contains(row.AccountId))
            .ToListAsync(cancellationToken)).ToLookup(row => row.AccountId);
        var gyms = (await db.HomeGyms.AsNoTracking().Where(row => accountIds.Contains(row.AccountId))
            .OrderBy(row => row.Position).ToListAsync(cancellationToken)).ToLookup(row => row.AccountId, row => row.GymId);

        // Rebuilt through the same rules they were saved under, from the grades as entered; the postcode was
        // checked when it was chosen.
        var read = new Dictionary<Guid, Profile>();
        foreach (var profile in profiles)
        {
            try
            {
                read[profile.AccountId] = Profile.Create(new ProfileDraft(
                    profile.DisplayName,
                    profile.Sex,
                    profile.AcceptedSexes,
                    profile.RopeScale,
                    Entries([.. variants[profile.AccountId]]),
                    profile.Certification,
                    profile.OwnGear,
                    profile.Postcode,
                    profile.MaxTravelKm,
                    [.. gyms[profile.AccountId]],
                    windows[profile.AccountId].Select(row => new WindowDraft(row.Day, row.StartHour, row.EndHour)).ToList()));
            }
            catch (DomainError error) when (invalid is not null)
            {
                invalid(profile.AccountId, error);
            }
        }

        return read;
    }

    public async Task SaveAsync(Guid accountId, Profile profile, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The upsert locks the profile row, so two saves of one profile run one after the other.
        var acceptedSexes = profile.AcceptedSexes.Order().Select(WireNames.Of).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO profiles (account_id, display_name, sex, accepted_sexes, rope_scale, certification, own_gear, postcode, max_travel_km)
            VALUES ({accountId}, {profile.DisplayName}, {WireNames.Of(profile.Sex)}, {acceptedSexes}, {WireNames.Of(profile.RopeScale)},
                    {WireNames.Of(profile.Certification)}, {profile.OwnGear}, {profile.Postcode}, {profile.MaxTravelKm})
            ON CONFLICT (account_id) DO UPDATE SET
                display_name = EXCLUDED.display_name, sex = EXCLUDED.sex, accepted_sexes = EXCLUDED.accepted_sexes,
                rope_scale = EXCLUDED.rope_scale, certification = EXCLUDED.certification, own_gear = EXCLUDED.own_gear,
                postcode = EXCLUDED.postcode, max_travel_km = EXCLUDED.max_travel_km
            """,
            cancellationToken);
        await db.DisciplineVariants.Where(row => row.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.AvailabilityWindows.Where(row => row.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.HomeGyms.Where(row => row.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        db.DisciplineVariants.AddRange(profile.Disciplines.SelectMany(entry => entry.Variants.Select(variant => new DisciplineVariantRow
        {
            AccountId = accountId,
            Variant = WireNames.Of(variant),
            Shared = entry.Venues.Count > 1,
            Scale = WireNames.Of(entry.Own.Lowest.Scale),
            OwnLowest = entry.Own.Lowest.Entered,
            OwnLowestPosition = entry.Own.Lowest.Position,
            OwnHighest = entry.Own.Highest.Entered,
            OwnHighestPosition = entry.Own.Highest.LastPosition,
            PartnerLowest = entry.Partner.Lowest.Entered,
            PartnerLowestPosition = entry.Partner.Lowest.Position,
            PartnerHighest = entry.Partner.Highest.Entered,
            PartnerHighestPosition = entry.Partner.Highest.LastPosition,
        })));
        db.AvailabilityWindows.AddRange(profile.Windows.Select(window => new AvailabilityWindowRow
        {
            AccountId = accountId,
            Day = WireNames.Of(window.Day),
            StartHour = window.StartHour,
            EndHour = window.EndHour,
        }));
        db.HomeGyms.AddRange(profile.HomeGyms.Select((gym, position) => new HomeGymRow
        {
            AccountId = accountId,
            GymId = gym,
            Position = position,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyDictionary<Guid, Gym>> FindGymsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new Dictionary<Guid, Gym>()
            : await db.Gyms.AsNoTracking().Where(gym => ids.Contains(gym.Id))
                .ToDictionaryAsync(gym => gym.Id, gym => new Gym(gym.Id, gym.Name, gym.Active), cancellationToken);

    // Shared variants of one discipline are one entry, as they were entered (S3).
    private static List<DisciplineEntryDraft?> Entries(List<DisciplineVariantRow> rows) =>
        rows.Select(row => (Row: row, Variant: WireNames.Parse<DisciplineVariant>(row.Variant)!.Value))
            .OrderBy(item => item.Variant)
            .GroupBy(item => (item.Variant.Discipline(), Alone: item.Row.Shared ? null : (DisciplineVariant?)item.Variant))
            .Select(group => (DisciplineEntryDraft?)new DisciplineEntryDraft(
                WireNames.Of(group.First().Variant.Discipline()),
                group.Select(item => WireNames.Of(item.Variant.Venue())).ToList(),
                new GradeRangeDraft(group.First().Row.OwnLowest, group.First().Row.OwnHighest),
                new GradeRangeDraft(group.First().Row.PartnerLowest, group.First().Row.PartnerHighest)))
            .ToList();
}
