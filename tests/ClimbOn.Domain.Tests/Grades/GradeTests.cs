using ClimbOn.Domain.Grades;
using Xunit;

namespace ClimbOn.Domain.Tests.Grades;

[Trait("SpecPartial", "S13")]
public sealed class GradeTests
{
    [Fact]
    [Trait("SpecPartial", "S13.1")]
    public void Rope_scales_are_Norwegian_and_French() =>
        Assert.Equal([GradeScale.Norwegian, GradeScale.French], GradeScales.For(GradeFamily.Rope));

    [Fact]
    [Trait("SpecPartial", "S13.2")]
    public void Bouldering_uses_Font_and_ice_uses_WI()
    {
        Assert.Equal([GradeScale.Font], GradeScales.For(GradeFamily.Bouldering));
        Assert.Equal([GradeScale.WaterIce], GradeScales.For(GradeFamily.Ice));
    }

    [Theory]
    [Trait("SpecPartial", "S13.3")]
    [InlineData(GradeScale.Norwegian, "3", GradeFamily.Rope, 0)]
    [InlineData(GradeScale.Norwegian, "7-", GradeFamily.Rope, 10)]
    [InlineData(GradeScale.Norwegian, "8", GradeFamily.Rope, 15)]
    [InlineData(GradeScale.Norwegian, "11+", GradeFamily.Rope, 29)]
    [InlineData(GradeScale.French, "3", GradeFamily.Rope, 0)]
    [InlineData(GradeScale.French, "6b", GradeFamily.Rope, 10)]
    [InlineData(GradeScale.French, "6b+", GradeFamily.Rope, 11)]
    [InlineData(GradeScale.French, "7b", GradeFamily.Rope, 16)]
    [InlineData(GradeScale.French, "9c", GradeFamily.Rope, 30)]
    [InlineData(GradeScale.Font, "3", GradeFamily.Bouldering, 0)]
    [InlineData(GradeScale.Font, "6A", GradeFamily.Bouldering, 5)]
    [InlineData(GradeScale.Font, "9A", GradeFamily.Bouldering, 23)]
    [InlineData(GradeScale.WaterIce, "WI1", GradeFamily.Ice, 0)]
    [InlineData(GradeScale.WaterIce, "WI4+", GradeFamily.Ice, 5)]
    [InlineData(GradeScale.WaterIce, "WI7", GradeFamily.Ice, 10)]
    public void Grade_keeps_entered_text_and_internal_position(
        GradeScale scale, string entered, GradeFamily family, int position)
    {
        Assert.True(Grade.TryCreate(scale, entered, out var grade));
        Assert.Equal(scale, grade.Scale);
        Assert.Equal(entered, grade.Entered);
        Assert.Equal(family, grade.Family);
        Assert.Equal(position, grade.Position);
    }

    [Theory]
    [Trait("SpecPartial", "S13.2")]
    [Trait("SpecPartial", "S13.3")]
    [InlineData(GradeScale.Norwegian, "6b")]
    [InlineData(GradeScale.Norwegian, "6A")]
    [InlineData(GradeScale.Norwegian, " 7")]
    [InlineData(GradeScale.Norwegian, "")]
    [InlineData(GradeScale.Norwegian, null)]
    [InlineData(GradeScale.French, "7-")]
    [InlineData(GradeScale.French, "6B")]
    [InlineData(GradeScale.Font, "6a")]
    [InlineData(GradeScale.Font, "WI4")]
    [InlineData(GradeScale.WaterIce, "wi4")]
    [InlineData(GradeScale.WaterIce, "4")]
    public void Grade_outside_its_scale_is_rejected(GradeScale scale, string? entered)
    {
        Assert.False(Grade.TryCreate(scale, entered, out var grade));
        Assert.Null(grade);
    }

    [Fact]
    [Trait("SpecPartial", "S13.3")]
    public void Grade_in_an_undefined_scale_is_rejected()
    {
        Assert.False(Grade.TryCreate((GradeScale)99, "3", out var grade));
        Assert.Null(grade);
    }

    [Theory]
    [Trait("SpecPartial", "S13.5")]
    [InlineData(GradeScale.Norwegian, "8", GradeScale.French, "7a+")]
    [InlineData(GradeScale.Norwegian, "7-", GradeScale.French, "6b")]
    [InlineData(GradeScale.Norwegian, "6", GradeScale.French, "6a")]
    [InlineData(GradeScale.Norwegian, "11+", GradeScale.French, "9b+")]
    [InlineData(GradeScale.French, "7b", GradeScale.Norwegian, "8")]
    [InlineData(GradeScale.French, "6b+", GradeScale.Norwegian, "7-")]
    [InlineData(GradeScale.French, "6a", GradeScale.Norwegian, "6-")]
    [InlineData(GradeScale.French, "9c", GradeScale.Norwegian, "11+")]
    [InlineData(GradeScale.Norwegian, "8", GradeScale.Norwegian, "8")]
    [InlineData(GradeScale.French, "6b+", GradeScale.French, "6b+")]
    [InlineData(GradeScale.Font, "7A", GradeScale.Norwegian, "7A")]
    [InlineData(GradeScale.Font, "7A", GradeScale.French, "7A")]
    [InlineData(GradeScale.WaterIce, "WI5", GradeScale.French, "WI5")]
    public void Other_climbers_see_rope_grades_in_their_own_scale(
        GradeScale scale, string entered, GradeScale viewerScale, string shown)
    {
        Assert.True(Grade.TryCreate(scale, entered, out var grade));
        Assert.Equal(shown, grade.ShownTo(viewerScale));
    }

    [Theory]
    [Trait("SpecPartial", "S13.5")]
    [InlineData(GradeScale.Font)]
    [InlineData(GradeScale.WaterIce)]
    public void Viewer_scale_must_be_a_rope_scale(GradeScale viewerScale)
    {
        Assert.True(Grade.TryCreate(GradeScale.Norwegian, "7", out var grade));
        Assert.Throws<ArgumentOutOfRangeException>(() => grade.ShownTo(viewerScale));
    }
}
