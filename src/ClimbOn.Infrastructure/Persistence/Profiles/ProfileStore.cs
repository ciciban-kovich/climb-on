using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace ClimbOn.Infrastructure.Persistence.Profiles;

public sealed class ProfileStore(ClimbOnDbContext db) : IProfileStore
{
    public async Task<Profile?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(row => row.AccountId == accountId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var variants = await db.DisciplineVariants.AsNoTracking().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken);
        var windows = await db.AvailabilityWindows.AsNoTracking().Where(row => row.AccountId == accountId).ToListAsync(cancellationToken);
        var gyms = await db.HomeGyms.AsNoTracking().Where(row => row.AccountId == accountId)
            .OrderBy(row => row.Position).Select(row => row.GymId).ToListAsync(cancellationToken);

        // Rebuilt through the same rules it was saved under; the postcode was checked when it was chosen.
        return Profile.Create(new ProfileDraft(
            profile.DisplayName,
            profile.Sex,
            profile.AcceptedSexes,
            profile.RopeScale,
            Entries(variants),
            profile.Certification,
            profile.OwnGear,
            profile.Postcode,
            profile.MaxTravelKm,
            gyms,
            windows.Select(row => new WindowDraft(row.Day, row.StartHour, row.EndHour)).ToList()));
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
            OwnHighestPosition = entry.Own.Highest.Position,
            PartnerLowest = entry.Partner.Lowest.Entered,
            PartnerLowestPosition = entry.Partner.Lowest.Position,
            PartnerHighest = entry.Partner.Highest.Entered,
            PartnerHighestPosition = entry.Partner.Highest.Position,
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

    public async Task<IReadOnlyDictionary<Guid, bool>> FindGymsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? new Dictionary<Guid, bool>()
            : await db.Gyms.AsNoTracking().Where(gym => ids.Contains(gym.Id)).ToDictionaryAsync(gym => gym.Id, gym => gym.Active, cancellationToken);

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
