using ClimbOn.Domain.Grades;
using ClimbOn.Domain.Profiles;
using Xunit;

namespace ClimbOn.Domain.Tests.Profiles;

public sealed class DisciplineVariantTests
{
    // S12, written out for every variant and certification. S12 stays partial until matching (task 17) uses it.
    public static TheoryData<DisciplineVariant, BelayCertification, bool> Requirement
    {
        get
        {
            var data = new TheoryData<DisciplineVariant, BelayCertification, bool>();
            foreach (var variant in Enum.GetValues<DisciplineVariant>())
            {
                foreach (var certification in Enum.GetValues<BelayCertification>())
                {
                    var met = variant switch
                    {
                        DisciplineVariant.TopRopeIndoor => certification != BelayCertification.None,
                        DisciplineVariant.LeadIndoor => certification == BelayCertification.BrattkortLed,
                        _ => true,
                    };
                    data.Add(variant, certification, met);
                }
            }

            return data;
        }
    }

    [Theory]
    [Trait("SpecPartial", "S12")]
    [MemberData(nameof(Requirement))]
    public void Certification_requirement_per_variant(DisciplineVariant variant, BelayCertification certification, bool met) =>
        Assert.Equal(met, variant.IsMetBy(certification));

    [Fact]
    [Trait("SpecPartial", "S12")]
    public void Undefined_certification_or_variant_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisciplineVariant.LeadIndoor.IsMetBy((BelayCertification)3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((DisciplineVariant)8).IsMetBy(BelayCertification.BrattkortLed));
    }

    // The specs glossary's eight variants.
    [Fact]
    public void There_are_eight_variants_of_five_disciplines()
    {
        Assert.Equal(
            [
                (Discipline.Bouldering, Venue.Indoor), (Discipline.Bouldering, Venue.Outdoor),
                (Discipline.TopRope, Venue.Indoor), (Discipline.TopRope, Venue.Outdoor),
                (Discipline.Lead, Venue.Indoor), (Discipline.Lead, Venue.Outdoor),
                (Discipline.Multipitch, Venue.Outdoor), (Discipline.Ice, Venue.Outdoor),
            ],
            DisciplineVariants.All.Select(variant => (variant.Discipline(), variant.Venue())));
        Assert.Null(DisciplineVariants.Of(Discipline.Multipitch, Venue.Indoor));
        Assert.Null(DisciplineVariants.Of(Discipline.Ice, Venue.Indoor));
        Assert.All(DisciplineVariants.All, variant => Assert.Equal(variant, DisciplineVariants.Of(variant.Discipline(), variant.Venue())));
    }

    [Theory]
    [Trait("Spec", "S13.1")]
    [Trait("Spec", "S13.2")]
    [InlineData(Discipline.TopRope, GradeFamily.Rope)]
    [InlineData(Discipline.Lead, GradeFamily.Rope)]
    [InlineData(Discipline.Multipitch, GradeFamily.Rope)]
    [InlineData(Discipline.Bouldering, GradeFamily.Bouldering)]
    [InlineData(Discipline.Ice, GradeFamily.Ice)]
    public void Each_discipline_is_graded_in_its_family(Discipline discipline, GradeFamily family) =>
        Assert.Equal(family, discipline.GradeFamily());
}
