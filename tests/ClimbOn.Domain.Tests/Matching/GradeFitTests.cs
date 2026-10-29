using ClimbOn.Domain.Matching;
using ClimbOn.Domain.Profiles;
using Xunit;
using static ClimbOn.Domain.Tests.Matching.Climbers;

namespace ClimbOn.Domain.Tests.Matching;

[Trait("Spec", "S60.2")]
public sealed class GradeFitTests
{
    // A climbs 6A-6B and wants 6B-7A; B is given here. Direction 1: A's own meets B's partner range.
    [Theory]
    [InlineData("6B", "7A", "6A", "6A+", 2)]
    [InlineData("6B", "7A", "7A+", "8A", 1)]
    [InlineData("7A+", "7B", "6A", "6A+", 1)]
    [InlineData("7A+", "7B", "7B", "8A", 0)]
    [InlineData("4", "5", "3", "5+", 0)]
    public void Fit_counts_the_directions_that_hold(string bOwnLow, string bOwnHigh, string bPartnerLow, string bPartnerHigh, int fit)
    {
        var a = Bouldering(Range("6A", "6B"), Range("6B", "7A"));
        var b = Bouldering(Range(bOwnLow, bOwnHigh), Range(bPartnerLow, bPartnerHigh));

        Assert.Equal(fit, GradeFit.Of(a, b));
        Assert.Equal(fit, GradeFit.Of(b, a));
    }

    // French 6a sits where Norwegian 6- does (S13.3): ranges on the two rope scales meet on the internal scale.
    [Fact]
    public void Rope_ranges_are_compared_on_the_internal_scale()
    {
        var a = Profile.Create(Draft with
        {
            Disciplines = [new("lead", ["outdoor"], Range("6-", "6-"), Range("6-", "6-"))],
        });
        var b = Profile.Create(Draft with
        {
            RopeScale = "french",
            Disciplines = [new("lead", ["outdoor"], Range("6a", "6a"), Range("6a+", "7a"))],
        });

        Assert.Equal(1, GradeFit.Of(a, b));
    }

    // Norwegian 7- spans the rows of French 6b and 6b+, and French 6a those of Norwegian 6- and 6: the fit is the
    // same whichever scale each climber chose.
    [Theory]
    [InlineData("norwegian", "7-", "7-", "norwegian", "7-", "8-", 2)]
    [InlineData("norwegian", "7-", "7-", "french", "6b+", "7a", 2)]
    [InlineData("french", "6a", "6a", "norwegian", "6", "6", 2)]
    [InlineData("french", "6a", "6a", "norwegian", "6+", "7", 0)]
    [InlineData("norwegian", "6", "6", "french", "6a+", "6b", 0)]
    public void Ranges_cover_every_row_their_grades_span(
        string aScale, string aLow, string aHigh, string bScale, string bLow, string bHigh, int fit)
    {
        // Each climber's own and partner range are the same, so both directions hold or neither does.
        var a = Profile.Create(Draft with
        {
            RopeScale = aScale,
            Disciplines = [new("lead", ["outdoor"], Range(aLow, aHigh), Range(aLow, aHigh))],
        });
        var b = Profile.Create(Draft with
        {
            RopeScale = bScale,
            Disciplines = [new("lead", ["outdoor"], Range(bLow, bHigh), Range(bLow, bHigh))],
        });

        Assert.Equal(fit, GradeFit.Of(a, b));
        Assert.Equal(fit, GradeFit.Of(b, a));
    }

    [Fact]
    public void Fit_is_the_best_over_the_qualifying_shared_variants()
    {
        var a = Profile.Create(Draft with
        {
            Disciplines =
            [
                new("bouldering", ["indoor"], Range("3", "4"), Range("3", "4")),
                new("lead", ["indoor", "outdoor"], Range("6", "7"), Range("6", "7")),
            ],
            Certification = "brattkort-led",
        });
        var b = Draft with
        {
            Disciplines =
            [
                new("bouldering", ["indoor"], Range("8A", "9A"), Range("8A", "9A")),
                new("lead", ["indoor"], Range("6", "7"), Range("6", "7")),
                new("lead", ["outdoor"], Range("6", "7"), Range("4", "5")),
            ],
        };

        // Lead indoor would give 2 but fails S12 for B; lead outdoor gives 1, bouldering 0.
        Assert.Equal(1, GradeFit.Of(a, Profile.Create(b)));
        Assert.Equal(2, GradeFit.Of(a, Profile.Create(b with { Certification = "brattkort-led" })));
    }

    private static Profile Bouldering(GradeRangeDraft own, GradeRangeDraft partner) =>
        Profile.Create(Draft with { Disciplines = [new("bouldering", ["indoor"], own, partner)] });
}
