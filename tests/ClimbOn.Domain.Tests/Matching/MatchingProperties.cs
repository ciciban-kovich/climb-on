using ClimbOn.Domain.Geo;
using ClimbOn.Domain.Grades;
using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace ClimbOn.Domain.Tests.Matching;

// C20: over randomly generated profiles. The generators keep every criterion near its edge: points within about
// 60 km of each other against travel distances of 1 to 100 km, three sexes, eight variants and three
// certifications, so that pairs fall on both sides of each criterion.
public sealed class MatchingProperties
{
    private static readonly Guid[] GymPool =
    [
        new("6f1c2a10-0000-4000-8000-000000000001"),
        new("6f1c2a10-0000-4000-8000-000000000002"),
        new("6f1c2a10-0000-4000-8000-000000000003"),
    ];

    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] Sexes = [.. Enum.GetValues<Sex>().Select(WireNames.Of)];

    // On two weekdays: up to two windows a day, cut from sorted distinct hours so that they never overlap (S61).
    private static readonly Gen<List<WindowDraft?>> AnyWindows =
        from tuesday in WindowsOn("tuesday")
        from saturday in WindowsOn("saturday")
        select (List<WindowDraft?>)[.. tuesday.Concat(saturday).DefaultIfEmpty(new WindowDraft("tuesday", 18, 21))];

    private static readonly Gen<Shape> AnyShape =
        from sex in Gen.Elements(Sexes)
        from accepted in Gen.SubListOf(Sexes).Select(list => list.Count == 0 ? null : list)
        from variants in Gen.SubListOf<DisciplineVariant>(DisciplineVariants.All).Where(list => list.Count > 0)
        from certification in Gen.Elements(Enum.GetValues<BelayCertification>().Select(WireNames.Of))
        from travel in Gen.Choose(Profile.MinimumTravelKm, Profile.MaximumTravelKm)
        from gyms in Gen.SubListOf(GymPool)
        from windows in AnyWindows
        select new Shape(
            new ProfileDraft(
                "Kari",
                sex,
                accepted?.Select(name => (string?)name).ToList(),
                null,
                null,
                certification,
                true,
                "0150",
                travel,
                gyms,
                windows),
            variants);

    // A random rope scale and grade ranges for a shape's variants.
    private static Gen<ProfileDraft> Graded(Shape shape) =>
        from ropeScale in Gen.Elements(GradeScale.Norwegian, GradeScale.French)
        from entries in Gen.CollectToList(shape.Variants, variant =>
            from own in AnyRange(Profile.ScaleOf(variant.Discipline(), ropeScale))
            from partner in AnyRange(Profile.ScaleOf(variant.Discipline(), ropeScale))
            select (DisciplineEntryDraft?)new DisciplineEntryDraft(
                WireNames.Of(variant.Discipline()), [WireNames.Of(variant.Venue())], own, partner))
        select shape.Draft with { RopeScale = WireNames.Of(ropeScale), Disciplines = entries };

    // Three points, so that equal distances are common, and anywhere in a box of about 60 by 55 km.
    private static readonly Gen<GeoPoint> AnyPoint = Gen.OneOf(
        Gen.Elements(new GeoPoint(59.9, 10.7), new GeoPoint(60.1, 10.9), new GeoPoint(60.2, 11.2)),
        from latitude in Gen.Choose(59_700, 60_250)
        from longitude in Gen.Choose(10_500, 11_500)
        select new GeoPoint(latitude / 1000.0, longitude / 1000.0));

    private static readonly Gen<Climber> AnyClimber =
        from shape in AnyShape
        from draft in Graded(shape)
        from point in AnyPoint
        from hoursAgo in Gen.Choose(0, 3)
        from id in Gen.Fresh(Guid.NewGuid)
        select new Climber(id, Profile.Create(draft), point, Noon.AddHours(-hoursAgo));

    private static readonly Gen<(Climber A, Climber B)> AnyPair =
        from a in AnyClimber
        from b in AnyClimber
        select (a, b);

    // A climber, and the same climber with every grade and the rope scale drawn again.
    private static readonly Arbitrary<(Climber A, Climber B, Climber Regraded)> AnyRegrading = Arb.From(
        from a in AnyClimber
        from shape in AnyShape
        from draft in Graded(shape)
        from regraded in Graded(shape)
        from point in AnyPoint
        from id in Gen.Fresh(Guid.NewGuid)
        select (a, new Climber(id, Profile.Create(draft), point, Noon), new Climber(id, Profile.Create(regraded), point, Noon)));

    // A requester and three candidates; a later one is often an earlier one under another account ID, so that every
    // key but the last is tied.
    private static readonly Arbitrary<(Climber Requester, Climber[] Candidates, HashSet<Guid> ActiveGyms)> AnyList =
        Arb.From(
            from requester in AnyClimber
            from first in AnyClimber
            from second in Gen.OneOf(AnyClimber, Twin(first))
            from third in Gen.OneOf(AnyClimber, Twin(first), Twin(second))
            from active in Gen.SubListOf(GymPool)
            select (requester, new[] { first, second, third }, active.ToHashSet()));

    [Property(MaxTest = 1000)]
    [Trait("SpecPartial", "S10")]
    [Trait("Spec", "S4")]
    [Trait("Spec", "S4.1")]
    [Trait("Spec", "S4.2")]
    [Trait("Spec", "S4.3")]
    public Property Candidate_rule_is_symmetric() =>
        Prop.ForAll(Arb.From(AnyPair), pair => CandidateRule.IsCandidate(pair.A, pair.B) == CandidateRule.IsCandidate(pair.B, pair.A));

    // The properties above prove nothing about a criterion the generated pairs always meet, or never do.
    [Fact]
    public void Generated_pairs_fall_on_both_sides_of_each_criterion()
    {
        var pairs = AnyPair.Sample(500);
        Func<(Climber A, Climber B), bool>[] criteria =
        [
            pair => CandidateRule.IsWithinTravel(pair.A, pair.B),
            pair => CandidateRule.AcceptEachOther(pair.A.Profile, pair.B.Profile),
            pair => CandidateRule.SharedVariants(pair.A.Profile, pair.B.Profile).Any(),
            pair => CandidateRule.IsCandidate(pair.A, pair.B),
        ];

        Assert.All(criteria, criterion => Assert.Equal(2, pairs.Select(criterion).Distinct().Count()));
    }

    [Property(MaxTest = 1000)]
    [Trait("Spec", "S14")]
    public Property Grades_never_change_candidacy() =>
        Prop.ForAll(AnyRegrading, entry =>
            CandidateRule.IsCandidate(entry.A, entry.B) == CandidateRule.IsCandidate(entry.A, entry.Regraded)
            && CandidateRule.IsCandidate(entry.B, entry.A) == CandidateRule.IsCandidate(entry.Regraded, entry.A));

    // Reflexive, antisymmetric, transitive, and equal only for one account: a total order on the candidates.
    [Property(MaxTest = 1000)]
    [Trait("Spec", "S60")]
    public Property Comparer_is_total_order() =>
        Prop.ForAll(AnyList, list =>
        {
            var keys = list.Candidates.Select(candidate => CandidateKey.For(list.Requester, candidate, list.ActiveGyms)).ToArray();
            var comparer = CandidateComparer.Instance;
            foreach (var x in keys)
            {
                Assert.Equal(0, comparer.Compare(x, x));
                foreach (var y in keys)
                {
                    var order = comparer.Compare(x, y);
                    Assert.Equal(Math.Sign(order), -Math.Sign(comparer.Compare(y, x)));
                    Assert.Equal(x.AccountId == y.AccountId, order == 0);
                    foreach (var z in keys.Where(z => order <= 0 && comparer.Compare(y, z) <= 0))
                    {
                        Assert.True(comparer.Compare(x, z) <= 0);
                    }
                }
            }
        });

    private static Gen<Climber> Twin(Climber climber) =>
        Gen.Fresh(Guid.NewGuid).Select(id => climber with { AccountId = id });

    private static Gen<GradeRangeDraft> AnyRange(GradeScale scale) =>
        from low in Gen.Choose(0, scale.Grades().Count - 1)
        from high in Gen.Choose(low, scale.Grades().Count - 1)
        select new GradeRangeDraft(scale.Grades()[low], scale.Grades()[high]);

    private static Gen<IEnumerable<WindowDraft>> WindowsOn(string day) =>
        from hours in Gen.SubListOf(Enumerable.Range(0, AvailabilityWindow.HoursPerDay + 1))
        from count in Gen.Choose(0, 2)
        select hours.Order().Take(count * 2).Chunk(2).Where(pair => pair.Length == 2)
            .Select(pair => new WindowDraft(day, pair[0], pair[1]));

    // The shape of a profile without its grades: each variant chosen is one entry of its own.
    private sealed record Shape(ProfileDraft Draft, IReadOnlyList<DisciplineVariant> Variants);
}
