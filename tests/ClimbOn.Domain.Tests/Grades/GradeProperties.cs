using ClimbOn.Domain.Grades;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace ClimbOn.Domain.Tests.Grades;

[Trait("SpecPartial", "S13")]
public sealed class GradeProperties
{
    private static readonly GradeScale[] RopeScales = [GradeScale.Norwegian, GradeScale.French];

    private static readonly Arbitrary<(GradeScale Scale, string Text)> AnyGrade = Arb.From(
        from scale in Gen.Elements(Enum.GetValues<GradeScale>())
        from text in Gen.Elements<string>(scale.Grades())
        select (scale, text));

    // Adjacent grades in the independently written order: monotonic steps imply monotonic over any pair.
    private static readonly Arbitrary<(GradeScale Scale, int Easier)> AdjacentGrades = Arb.From(
        from scale in Gen.Elements(Enum.GetValues<GradeScale>())
        from easier in Gen.Choose(0, ExpectedGrades.For(scale).Count - 2)
        select (scale, easier));

    private static readonly Arbitrary<(GradeScale Scale, int Easier)> AdjacentRopeGrades = Arb.From(
        from scale in Gen.Elements(RopeScales)
        from easier in Gen.Choose(0, ExpectedGrades.For(scale).Count - 2)
        select (scale, easier));

    [Property]
    [Trait("SpecPartial", "S13.4")]
    public Property Owner_view_equals_entered_text() =>
        Prop.ForAll(AnyGrade, entry =>
            Grade.TryCreate(entry.Scale, entry.Text, out var grade)
            && grade.OwnerView == entry.Text
            && (grade.Family != GradeFamily.Rope || grade.ShownTo(entry.Scale) == entry.Text));

    [Property(MaxTest = 1000)]
    [Trait("SpecPartial", "S13.3")]
    public Property Harder_grade_has_higher_position() =>
        Prop.ForAll(AdjacentGrades, step =>
        {
            var grades = ExpectedGrades.For(step.Scale);
            Assert.True(Grade.TryCreate(step.Scale, grades[step.Easier], out var easier));
            Assert.True(Grade.TryCreate(step.Scale, grades[step.Easier + 1], out var harder));
            return easier.Position < harder.Position;
        });

    [Property(MaxTest = 1000)]
    [Trait("SpecPartial", "S13.3")]
    [Trait("SpecPartial", "S13.5")]
    public Property Rope_conversion_is_monotonic() =>
        Prop.ForAll(AdjacentRopeGrades, step =>
        {
            var grades = ExpectedGrades.For(step.Scale);
            var other = step.Scale == GradeScale.Norwegian ? GradeScale.French : GradeScale.Norwegian;
            Assert.True(Grade.TryCreate(step.Scale, grades[step.Easier], out var easier));
            Assert.True(Grade.TryCreate(step.Scale, grades[step.Easier + 1], out var harder));

            var otherGrades = ExpectedGrades.For(other).ToList();
            var easierShown = otherGrades.IndexOf(easier.ShownTo(other));
            var harderShown = otherGrades.IndexOf(harder.ShownTo(other));

            return easierShown >= 0 && easierShown <= harderShown;
        });
}
