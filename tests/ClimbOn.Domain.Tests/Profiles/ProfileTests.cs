using ClimbOn.Domain.Errors;
using ClimbOn.Domain.Grades;
using ClimbOn.Domain.Postcodes;
using ClimbOn.Domain.Profiles;
using Xunit;

namespace ClimbOn.Domain.Tests.Profiles;

[Trait("SpecPartial", "S3")]
public sealed class ProfileTests
{
    private static readonly Guid Gym = new("6f1c2a10-0000-4000-8000-000000000001");

    private static readonly ProfileDraft Valid = new(
        "Kari",
        "female",
        null,
        "norwegian",
        [
            new DisciplineEntryDraft("bouldering", ["indoor"], new GradeRangeDraft("5", "6B"), new GradeRangeDraft("4", "7A")),
            new DisciplineEntryDraft("lead", ["outdoor", "indoor"], new GradeRangeDraft("6-", "7"), new GradeRangeDraft("5", "8-")),
        ],
        "brattkort-led",
        true,
        "0150",
        25,
        [Gym, Gym],
        [new WindowDraft("tuesday", 18, 21)]);

    [Fact]
    public void Valid_draft_becomes_a_profile()
    {
        var profile = Profile.Create(Valid);

        Assert.Equal("Kari", profile.DisplayName);
        Assert.Equal(Sex.Female, profile.Sex);
        Assert.Equal([Sex.Female, Sex.Male, Sex.Other], profile.AcceptedSexes.Order());
        Assert.Equal(GradeScale.Norwegian, profile.RopeScale);
        Assert.Equal(BelayCertification.BrattkortLed, profile.Certification);
        Assert.True(profile.OwnGear);
        Assert.Equal("0150", profile.Postcode);
        Assert.Equal(25, profile.MaxTravelKm);
        Assert.Equal([Gym], profile.HomeGyms);
        Assert.Equal([new AvailabilityWindow(DayOfWeek.Tuesday, 18, 21)], profile.Windows);
        Assert.Equal(
            [DisciplineVariant.BoulderingIndoor, DisciplineVariant.LeadIndoor, DisciplineVariant.LeadOutdoor],
            profile.Variants);
        var lead = profile.Disciplines[1];
        Assert.Equal([Venue.Indoor, Venue.Outdoor], lead.Venues);
        Assert.Equal(("6-", "7"), (lead.Own.Lowest.Entered, lead.Own.Highest.Entered));
    }

    [Theory]
    [InlineData("female", Sex.Female)]
    [InlineData("male", Sex.Male)]
    [InlineData("other", Sex.Other)]
    public void Accepted_sexes_are_the_chosen_ones(string name, Sex sex) =>
        Assert.Equal(new[] { sex }, Profile.Create(Valid with { AcceptedSexes = [name, name] }).AcceptedSexes);

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("norwegian", GradeScale.Norwegian, "7-", "8")]
    [InlineData("french", GradeScale.French, "6b", "7a+")]
    public void Rope_grades_are_on_the_chosen_scale(string name, GradeScale scale, string lowest, string highest)
    {
        var range = new GradeRangeDraft(lowest, highest);
        var profile = Profile.Create(Valid with
        {
            RopeScale = name,
            Disciplines = new[] { "top-rope", "lead" }.Select(discipline => (DisciplineEntryDraft?)new DisciplineEntryDraft(discipline, ["indoor"], range, range))
                .Append(new DisciplineEntryDraft("multipitch", ["outdoor"], range, range)).ToList(),
        });

        Assert.Equal(scale, profile.RopeScale);
        Assert.All(profile.Disciplines, entry => Assert.Equal(scale, entry.Own.Lowest.Scale));
    }

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("norwegian", "6a")]
    [InlineData("french", "7-")]
    public void Rope_grade_on_the_other_rope_scale_is_rejected(string scale, string grade) =>
        AssertRejected(
            Profile.GradeInvalidCode,
            Valid with { RopeScale = scale, Disciplines = [Entry("lead", "indoor", grade, grade)] });

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("font")]
    [InlineData("water-ice")]
    [InlineData("French")]
    [InlineData("")]
    [InlineData(null)]
    public void Rope_scale_is_Norwegian_or_French(string? scale) =>
        AssertRejected(Profile.RopeScaleInvalidCode, Valid with { RopeScale = scale });

    [Theory]
    [Trait("Spec", "S13.2")]
    [InlineData("norwegian", "bouldering", "6A+", true)]
    [InlineData("french", "bouldering", "6A+", true)]
    [InlineData("norwegian", "bouldering", "6", false)]
    [InlineData("french", "bouldering", "6a", false)]
    [InlineData("norwegian", "bouldering", "WI3", false)]
    [InlineData("french", "ice", "WI3", true)]
    [InlineData("norwegian", "ice", "6A", false)]
    [InlineData("french", "ice", "6a", false)]
    public void Bouldering_takes_Font_and_ice_takes_WI_whatever_the_rope_scale(string ropeScale, string discipline, string grade, bool accepted)
    {
        var draft = Valid with
        {
            RopeScale = ropeScale,
            Disciplines = [Entry(discipline, discipline == "ice" ? "outdoor" : "indoor", grade, grade)],
        };

        if (accepted)
        {
            var range = Assert.Single(Profile.Create(draft).Disciplines).Own;
            Assert.Equal(discipline == "ice" ? GradeScale.WaterIce : GradeScale.Font, range.Lowest.Scale);
        }
        else
        {
            AssertRejected(Profile.GradeInvalidCode, draft);
        }
    }

    [Theory]
    [Trait("Spec", "S13.3")]
    [InlineData("norwegian", "lead", "7-", 10)]
    [InlineData("french", "lead", "6b+", 11)]
    [InlineData("norwegian", "bouldering", "6A", 5)]
    [InlineData("norwegian", "ice", "WI4+", 5)]
    public void Grades_keep_their_entered_text_and_internal_position(string ropeScale, string discipline, string grade, int position)
    {
        var range = Assert.Single(Profile.Create(Valid with
        {
            RopeScale = ropeScale,
            Disciplines = [Entry(discipline, discipline == "ice" ? "outdoor" : "indoor", grade, grade)],
        }).Disciplines).Partner;

        Assert.Equal((grade, position), (range.Lowest.Entered, range.Lowest.Position));
        Assert.Equal((grade, position), (range.Highest.Entered, range.Highest.Position));
    }

    [Theory]
    [InlineData("6-", "6-", true)]
    [InlineData("6-", "6", true)]
    [InlineData("6", "6-", false)]
    [InlineData("7", "6+", false)]
    public void Range_runs_from_easier_to_harder(string lowest, string highest, bool accepted)
    {
        var draft = Valid with { Disciplines = [Entry("top-rope", "outdoor", lowest, highest)] };

        if (accepted)
        {
            Profile.Create(draft);
        }
        else
        {
            AssertRejected(Profile.GradeRangeInvalidCode, draft);
        }
    }

    public static TheoryData<string, string> InvalidDrafts => new()
    {
        { "display-name-null", Profile.DisplayNameInvalidCode },
        { "display-name-blank", Profile.DisplayNameInvalidCode },
        { "display-name-51", Profile.DisplayNameInvalidCode },
        { "display-name-tab", Profile.DisplayNameInvalidCode },
        { "sex-null", Profile.SexInvalidCode },
        { "sex-capitalised", Profile.SexInvalidCode },
        { "accepted-empty", Profile.AcceptedSexesInvalidCode },
        { "accepted-null-item", Profile.AcceptedSexesInvalidCode },
        { "entry-null", Profile.VariantInvalidCode },
        { "discipline-null", Profile.VariantInvalidCode },
        { "venues-null", Profile.VariantInvalidCode },
        { "venue-unknown", Profile.VariantInvalidCode },
        { "multipitch-indoor", Profile.VariantInvalidCode },
        { "same-variant-twice", Profile.VariantDuplicateCode },
        { "own-missing", Profile.GradeInvalidCode },
        { "partner-highest-missing", Profile.GradeInvalidCode },
        { "certification-null", Profile.CertificationInvalidCode },
        { "own-gear-null", Profile.OwnGearRequiredCode },
        { "postcode-null", PostcodeRules.MalformedCode },
        { "postcode-letters", PostcodeRules.MalformedCode },
        { "travel-0", Profile.TravelDistanceInvalidCode },
        { "travel-101", Profile.TravelDistanceInvalidCode },
        { "travel-null", Profile.TravelDistanceInvalidCode },
        { "window-null", AvailabilityWindow.InvalidCode },
        { "window-day-unknown", AvailabilityWindow.InvalidCode },
        { "window-start-null", AvailabilityWindow.InvalidCode },
        { "windows-null", AvailabilityWindow.RequiredCode },
    };

    [Theory]
    [MemberData(nameof(InvalidDrafts))]
    public void Invalid_field_is_rejected_with_its_code(string field, string code)
    {
        var grades = new GradeRangeDraft("5", "6A");
        AssertRejected(code, field switch
        {
            "display-name-null" => Valid with { DisplayName = null },
            "display-name-blank" => Valid with { DisplayName = " \t " },
            "display-name-51" => Valid with { DisplayName = new string('k', 51) },
            "display-name-tab" => Valid with { DisplayName = "Kari\tNordmann" },
            "sex-null" => Valid with { Sex = null },
            "sex-capitalised" => Valid with { Sex = "Female" },
            "accepted-empty" => Valid with { AcceptedSexes = [] },
            "accepted-null-item" => Valid with { AcceptedSexes = ["male", null] },
            "entry-null" => Valid with { Disciplines = [null] },
            "discipline-null" => Valid with { Disciplines = [new DisciplineEntryDraft(null, ["indoor"], grades, grades)] },
            "venues-null" => Valid with { Disciplines = [new DisciplineEntryDraft("bouldering", null, grades, grades)] },
            "venue-unknown" => Valid with { Disciplines = [new DisciplineEntryDraft("bouldering", ["gym"], grades, grades)] },
            "multipitch-indoor" => Valid with { Disciplines = [Entry("multipitch", "indoor", "6", "7")] },
            "same-variant-twice" => Valid with { Disciplines = [Entry("bouldering", "indoor", "5", "6A"), Entry("bouldering", "indoor", "5", "6A")] },
            "own-missing" => Valid with { Disciplines = [new DisciplineEntryDraft("bouldering", ["indoor"], null, grades)] },
            "partner-highest-missing" => Valid with { Disciplines = [new DisciplineEntryDraft("bouldering", ["indoor"], grades, new GradeRangeDraft("5", null))] },
            "certification-null" => Valid with { Certification = null },
            "own-gear-null" => Valid with { OwnGear = null },
            "postcode-null" => Valid with { Postcode = null },
            "postcode-letters" => Valid with { Postcode = "01a0" },
            "travel-0" => Valid with { MaxTravelKm = 0 },
            "travel-101" => Valid with { MaxTravelKm = 101 },
            "travel-null" => Valid with { MaxTravelKm = null },
            "window-null" => Valid with { Windows = [null] },
            "window-day-unknown" => Valid with { Windows = [new WindowDraft("someday", 18, 20)] },
            "window-start-null" => Valid with { Windows = [new WindowDraft("monday", null, 20)] },
            "windows-null" => Valid with { Windows = null },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        });
    }

    [Theory]
    [InlineData("K", "K")]
    [InlineData("  Kari  ", "Kari")]
    [InlineData("Åse Ødegård", "Åse Ødegård")]
    public void Display_name_is_trimmed(string written, string stored) =>
        Assert.Equal(stored, Profile.Create(Valid with { DisplayName = written }).DisplayName);

    // Counted in characters as the climber sees them: 50 letters with a combining accent each are 100 chars.
    [Fact]
    public void Display_name_of_50_characters_is_accepted()
    {
        Assert.Equal(50, Profile.Create(Valid with { DisplayName = new string('k', 50) }).DisplayName.Length);
        Assert.Equal(100, Profile.Create(Valid with { DisplayName = string.Concat(Enumerable.Repeat("é", 50)) }).DisplayName.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Travel_distance_is_1_to_100_km(int km) =>
        Assert.Equal(km, Profile.Create(Valid with { MaxTravelKm = km }).MaxTravelKm);

    [Fact]
    public void Profile_may_list_no_disciplines_and_no_gyms()
    {
        var profile = Profile.Create(Valid with { Disciplines = null, HomeGyms = null });

        Assert.Empty(profile.Disciplines);
        Assert.Empty(profile.HomeGyms);
    }

    [Theory]
    [InlineData(Discipline.TopRope, "top-rope")]
    [InlineData(BelayCertification.BrattkortTopptau, "brattkort-topptau")]
    [InlineData(DayOfWeek.Sunday, "sunday")]
    [InlineData(GradeScale.WaterIce, "water-ice")]
    public void Wire_names_are_lower_case_with_hyphens(object value, string name)
    {
        var parsed = value switch
        {
            Discipline discipline => (WireNames.Of(discipline), (object?)WireNames.Parse<Discipline>(name)),
            BelayCertification certification => (WireNames.Of(certification), WireNames.Parse<BelayCertification>(name)),
            DayOfWeek day => (WireNames.Of(day), WireNames.Parse<DayOfWeek>(name)),
            GradeScale scale => (WireNames.Of(scale), WireNames.Parse<GradeScale>(name)),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

        Assert.Equal((name, value), parsed);
        Assert.Null(WireNames.Parse<Discipline>("TopRope"));
        Assert.Null(WireNames.Parse<Discipline>(null));
    }

    private static DisciplineEntryDraft Entry(string discipline, string venue, string lowest, string highest) =>
        new(discipline, [venue], new GradeRangeDraft(lowest, highest), new GradeRangeDraft(lowest, highest));

    private static void AssertRejected(string code, ProfileDraft draft) =>
        Assert.Equal(code, Assert.Throws<DomainError>(() => Profile.Create(draft)).Code);
}
