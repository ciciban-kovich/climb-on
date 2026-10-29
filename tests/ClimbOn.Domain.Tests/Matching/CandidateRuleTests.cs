using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using Xunit;
using static ClimbOn.Domain.Tests.Matching.Climbers;

namespace ClimbOn.Domain.Tests.Matching;

[Trait("SpecPartial", "S4")]
public sealed class CandidateRuleTests
{
    [Fact]
    public void Climbers_meeting_every_criterion_are_candidates() =>
        Assert.True(CandidateRule.IsCandidate(Climber(), Climber()));

    [Theory]
    [Trait("SpecPartial", "S4.1")]
    [Trait("SpecPartial", "S16")]
    [InlineData(10, 30, 9.9, true)]
    [InlineData(10, 30, 10.4, true)]
    [InlineData(10, 30, 10.6, false)]
    [InlineData(30, 10, 10.6, false)]
    [InlineData(30, 30, 30.4, true)]
    [InlineData(30, 30, 30.6, false)]
    public void Rounded_distance_is_at_most_the_smaller_travel_distance(int aKm, int bKm, double apartKm, bool candidate)
    {
        var a = Climber(Draft with { MaxTravelKm = aKm });
        var b = Climber(Draft with { MaxTravelKm = bKm }, NorthOfOslo(apartKm));

        Assert.Equal(apartKm, CandidateRule.DistanceKm(a, b), 6);
        Assert.Equal(candidate, CandidateRule.IsCandidate(a, b));
    }

    [Theory]
    [Trait("SpecPartial", "S4.1")]
    [Trait("SpecPartial", "S52")]
    [InlineData(10.4, 10, 30, true)]
    [InlineData(10.5, 10, 30, false)]
    [InlineData(10.5, 30, 10, false)]
    [InlineData(11.5, 12, 30, true)]
    public void Distance_rounds_halves_up_before_the_travel_limit(double km, int maxA, int maxB, bool within) =>
        Assert.Equal(within, CandidateRule.IsWithinTravel(km, maxA, maxB));

    [Theory]
    [Trait("SpecPartial", "S4.2")]
    [InlineData("female", null, "male", null, true)]
    [InlineData("female", new[] { "male" }, "male", new[] { "female" }, true)]
    [InlineData("female", new[] { "female" }, "male", null, false)]
    [InlineData("female", null, "male", new[] { "male", "other" }, false)]
    [InlineData("other", new[] { "other" }, "other", new[] { "other" }, true)]
    public void Each_accepts_the_others_sex(string aSex, string[]? aAccepts, string bSex, string[]? bAccepts, bool candidate)
    {
        var a = Climber(Draft with { Sex = aSex, AcceptedSexes = aAccepts });
        var b = Climber(Draft with { Sex = bSex, AcceptedSexes = bAccepts });

        Assert.Equal(candidate, CandidateRule.IsCandidate(a, b));
    }

    [Theory]
    [Trait("SpecPartial", "S4.3")]
    [Trait("SpecPartial", "S12")]
    [InlineData("top-rope", "indoor", "brattkort-topptau", "brattkort-led", true)]
    [InlineData("top-rope", "indoor", "none", "brattkort-led", false)]
    [InlineData("lead", "indoor", "brattkort-led", "brattkort-led", true)]
    [InlineData("lead", "indoor", "brattkort-led", "brattkort-topptau", false)]
    [InlineData("lead", "outdoor", "none", "none", true)]
    [InlineData("top-rope", "outdoor", "none", "none", true)]
    public void A_shared_variant_needs_both_certifications(
        string discipline, string venue, string aCertification, string bCertification, bool candidate)
    {
        DisciplineEntryDraft[] rope = [new(discipline, [venue], Range("5", "6"), Range("5", "6"))];
        var a = Climber(Draft with { Disciplines = rope, Certification = aCertification });
        var b = Climber(Draft with { Disciplines = rope, Certification = bCertification });

        Assert.Equal(candidate, CandidateRule.IsCandidate(a, b));
    }

    [Fact]
    [Trait("SpecPartial", "S4.3")]
    public void Variants_of_one_discipline_at_other_venues_are_not_shared()
    {
        var a = Climber(Draft with { Disciplines = [new("bouldering", ["outdoor"], Range("5", "6B"), Range("5", "6B"))] });

        Assert.False(CandidateRule.IsCandidate(a, Climber()));
    }

    // One variant fails S12 for B; the other is shared and needs nothing.
    [Fact]
    [Trait("SpecPartial", "S4.3")]
    [Trait("SpecPartial", "S12")]
    public void One_qualifying_shared_variant_is_enough()
    {
        DisciplineEntryDraft[] both =
        [
            new("bouldering", ["indoor"], Range("5", "6B"), Range("5", "6B")),
            new("lead", ["indoor"], Range("5", "6"), Range("5", "6")),
        ];
        var a = Climber(Draft with { Disciplines = both, Certification = "brattkort-led" });
        var b = Climber(Draft with { Disciplines = both, Certification = "none" });

        Assert.Equal([DisciplineVariant.BoulderingIndoor], CandidateRule.SharedVariants(a.Profile, b.Profile));
        Assert.True(CandidateRule.IsCandidate(a, b));
    }

    [Fact]
    [Trait("SpecPartial", "S14")]
    public void Grade_ranges_that_never_meet_do_not_prevent_candidacy()
    {
        var a = Climber(Draft with { Disciplines = [new("bouldering", ["indoor"], Range("3", "4"), Range("3", "4"))] });
        var b = Climber(Draft with { Disciplines = [new("bouldering", ["indoor"], Range("8C", "9A"), Range("8C", "9A"))] });

        Assert.Equal(0, GradeFit.Of(a.Profile, b.Profile));
        Assert.True(CandidateRule.IsCandidate(a, b));
    }
}
