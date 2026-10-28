using System.Net;
using System.Net.Http.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Profiles;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Postcodes;
using ClimbOn.Domain.Profiles;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Profiles;

// S3 without the photo (M2), through GET and PUT /api/climber/profile.
public sealed class ProfileTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Route = "/api/climber/profile";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Climber_creates_a_profile_and_reads_it_back()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account);

        using var saved = await PutAsync(token, request);
        using var read = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var expected = Expected(request);
        AssertProfile(expected, await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct));
        AssertProfile(expected, await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct));
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Saving_again_replaces_the_whole_profile()
    {
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var changed = Factory.Data.Profile(account, "5003") with
        {
            Sex = "other",
            AcceptedSexes = ["male", "other"],
            RopeScale = "french",
            Disciplines =
            [
                new DisciplineEntryRequest("top-rope", ["indoor"], new GradeRangeRequest("5c", "6b+"), new GradeRangeRequest("5a", "7a")),
                new DisciplineEntryRequest("top-rope", ["outdoor"], new GradeRangeRequest("5a", "6a"), new GradeRangeRequest("4a", "6c")),
                new DisciplineEntryRequest("ice", ["outdoor"], new GradeRangeRequest("WI3", "WI4+"), new GradeRangeRequest("WI2", "WI5")),
            ],
            Certification = "brattkort-topptau",
            OwnGear = false,
            MaxTravelKm = 100,
            HomeGyms = [FixtureGyms.Active[2].Id, FixtureGyms.Active[1].Id],
            Windows = [new WindowRequest("saturday", 8, 16), new WindowRequest("monday", 0, 24)],
        };

        using var saved = await PutAsync(token, changed);
        using var read = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AssertProfile(Expected(changed), await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct));
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Accepted_partner_sexes_default_to_any()
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var saved = await PutAsync(token, Factory.Data.Profile(account) with { AcceptedSexes = null });

        Assert.Equal(["female", "male", "other"], (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.AcceptedSexes);
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Ranges_shared_across_venues_stay_one_entry_and_separate_ones_stay_two()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with
        {
            Disciplines =
            [
                new DisciplineEntryRequest("bouldering", ["outdoor", "indoor"], new GradeRangeRequest("5", "6A"), new GradeRangeRequest("5", "6A")),
                new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest("6", "7"), new GradeRangeRequest("6", "7")),
                new DisciplineEntryRequest("lead", ["outdoor"], new GradeRangeRequest("5", "6+"), new GradeRangeRequest("5", "6+")),
            ],
        };

        using var saved = await PutAsync(token, request);
        using var read = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var disciplines = (await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Disciplines;
        Assert.Equal(3, disciplines.Count);
        var bouldering = Assert.Single(disciplines, entry => entry.Discipline == "bouldering");
        Assert.Equal(["indoor", "outdoor"], bouldering.Venues);
        Assert.Equal(["indoor", "outdoor"], disciplines.Where(entry => entry.Discipline == "lead").Select(entry => Assert.Single(entry.Venues)));
        Assert.Equal(["shared", "shared", "alone", "alone"], await VariantColumnAsync(account, "CASE WHEN shared THEN 'shared' ELSE 'alone' END"));
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Entries_come_back_in_one_order_from_save_and_read()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with
        {
            Disciplines =
            [
                new DisciplineEntryRequest("ice", ["outdoor"], Ice, Ice),
                new DisciplineEntryRequest("lead", ["outdoor", "indoor"], new GradeRangeRequest("6", "7"), new GradeRangeRequest("6", "7")),
                new DisciplineEntryRequest("bouldering", ["outdoor"], new GradeRangeRequest("5", "6A"), new GradeRangeRequest("5", "6A")),
            ],
        };

        using var saved = await PutAsync(token, request);
        using var read = await GetAsync(token);

        string[] expected = ["bouldering outdoor", "lead indoor+outdoor", "ice outdoor"];
        Assert.Equal(expected, Order(await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct)));
        Assert.Equal(expected, Order(await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct)));

        static IEnumerable<string> Order(ProfileResponse? profile) =>
            profile!.Disciplines.Select(entry => $"{entry.Discipline} {string.Join('+', entry.Venues)}");
    }

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("norwegian", "6+", "7+")]
    [InlineData("french", "6a+", "7a")]
    public async Task Climber_chooses_Norwegian_or_French_for_rope_grades(string scale, string lowest, string highest)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var range = new GradeRangeRequest(lowest, highest);
        var request = Factory.Data.Profile(account) with
        {
            RopeScale = scale,
            Disciplines = new[] { "top-rope", "lead", "multipitch" }.Select(discipline => new DisciplineEntryRequest(
                discipline, discipline == "multipitch" ? new[] { "outdoor" } : new[] { "indoor", "outdoor" }, range, range)).ToList(),
        };

        using var saved = await PutAsync(token, request);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var profile = (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!;
        Assert.Equal(scale, profile.RopeScale);
        Assert.All(profile.Disciplines, entry => Assert.Equal(new GradeRangeResponse(lowest, highest), entry.Own));
    }

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("norwegian", "6a")] // French
    [InlineData("french", "7-")] // Norwegian
    [InlineData("norwegian", "6A")] // Font
    public async Task Rope_grade_off_the_chosen_scale_is_rejected(string scale, string grade)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with
        {
            RopeScale = scale,
            Disciplines = [new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest(grade, grade), new GradeRangeRequest(grade, grade))],
        };

        using var response = await PutAsync(token, request);

        await AssertRejectedAsync(response, Profile.GradeInvalidCode);
    }

    [Theory]
    [Trait("Spec", "S13.1")]
    [InlineData("font")]
    [InlineData("water-ice")]
    [InlineData("Norwegian")]
    [InlineData(null)]
    public async Task Rope_scale_is_Norwegian_or_French(string? scale)
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var response = await PutAsync(token, Factory.Data.Profile(account) with { RopeScale = scale });

        await AssertRejectedAsync(response, Profile.RopeScaleInvalidCode);
    }

    [Theory]
    [Trait("Spec", "S13.2")]
    [InlineData("bouldering", "outdoor", "6A+", "7B", true)]
    [InlineData("bouldering", "indoor", "6a", "6a", false)] // French
    [InlineData("bouldering", "indoor", "6", "6", false)] // Norwegian
    [InlineData("bouldering", "indoor", "WI3", "WI3", false)]
    [InlineData("ice", "outdoor", "WI3", "WI5+", true)]
    [InlineData("ice", "outdoor", "6A", "6A", false)]
    [InlineData("ice", "outdoor", "6", "6", false)]
    public async Task Bouldering_takes_Font_and_ice_takes_WI(string discipline, string venue, string lowest, string highest, bool accepted)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var range = new GradeRangeRequest(lowest, highest);
        var request = Factory.Data.Profile(account) with { Disciplines = [new DisciplineEntryRequest(discipline, [venue], range, range)] };

        using var response = await PutAsync(token, request);

        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            await AssertRejectedAsync(response, Profile.GradeInvalidCode);
        }
    }

    // Positions: Font 6A+ is 6 and 7B is 13; Norwegian 7- is row 10, 8 row 15; French 6b+ row 11, 7b row 16; WI4 is 4.
    [Theory]
    [Trait("Spec", "S13.3")]
    [InlineData("norwegian", "lead", "7-", 10, "8", 15)]
    [InlineData("french", "lead", "6b+", 11, "7b", 16)]
    [InlineData("norwegian", "bouldering", "6A+", 6, "7B", 13)]
    [InlineData("norwegian", "ice", "WI4", 4, "WI4", 4)]
    public async Task Grades_are_stored_as_entered_and_as_internal_positions(
        string scale, string discipline, string lowest, int lowestPosition, string highest, int highestPosition)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var venue = discipline == "ice" ? "outdoor" : "indoor";
        var range = new GradeRangeRequest(lowest, highest);
        var request = Factory.Data.Profile(account) with
        {
            RopeScale = scale,
            Disciplines = [new DisciplineEntryRequest(discipline, [venue], range, range)],
        };

        using var saved = await PutAsync(token, request);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(
            [$"{lowest}:{lowestPosition}:{highest}:{highestPosition}:{lowest}:{lowestPosition}:{highest}:{highestPosition}"],
            await VariantColumnAsync(
                account,
                "own_lowest || ':' || own_lowest_position || ':' || own_highest || ':' || own_highest_position || ':' || "
                + "partner_lowest || ':' || partner_lowest_position || ':' || partner_highest || ':' || partner_highest_position"));
    }

    // S13.4 stays partial until the data export (S63, M2) shows grades to their owner too.
    [Theory]
    [Trait("SpecPartial", "S13.4")]
    [InlineData("norwegian", "7-", "8")]
    [InlineData("french", "6b", "6b+")]
    [InlineData("french", "6a", "6a+")]
    public async Task Owner_reads_grades_exactly_as_entered(string scale, string lowest, string highest)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var range = new GradeRangeRequest(lowest, highest);
        using var saved = await PutAsync(token, Factory.Data.Profile(account) with
        {
            RopeScale = scale,
            Disciplines = [new DisciplineEntryRequest("lead", ["outdoor"], range, range)],
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var read = await GetAsync(token);

        var entry = Assert.Single((await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Disciplines);
        Assert.Equal(new GradeRangeResponse(lowest, highest), entry.Own);
        Assert.Equal(new GradeRangeResponse(lowest, highest), entry.Partner);
    }

    [Theory]
    [Trait("Spec", "S15")]
    [Trait("Spec", "S102.2")]
    [InlineData(FixturePostcodes.Retired, PostcodeRules.RetiredCode)]
    [InlineData(FixturePostcodes.Unknown, PostcodeRules.UnknownCode)]
    [InlineData("015", PostcodeRules.MalformedCode)]
    public async Task Profile_cannot_be_created_with_a_postcode_the_table_refuses(string postcode, string code)
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var response = await PutAsync(token, Factory.Data.Profile(account, postcode));
        using var read = await GetAsync(token);

        await AssertRejectedAsync(response, code);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Theory]
    [Trait("Spec", "S15")]
    [Trait("Spec", "S102.2")]
    [InlineData(FixturePostcodes.Retired, PostcodeRules.RetiredCode)]
    [InlineData(FixturePostcodes.Unknown, PostcodeRules.UnknownCode)]
    [InlineData("01500", PostcodeRules.MalformedCode)]
    public async Task Postcode_cannot_be_changed_to_one_the_table_refuses(string postcode, string code)
    {
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account, "0150"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var response = await PutAsync(token, Factory.Data.Profile(account, postcode));
        using var read = await GetAsync(token);

        await AssertRejectedAsync(response, code);
        Assert.Equal("0150", (await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Postcode);
    }

    [Fact]
    [Trait("Spec", "S15")]
    public async Task Postcode_can_be_changed_to_another_active_one()
    {
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account, "0150"));

        using var changed = await PutAsync(token, Factory.Data.Profile(account, "9008"));

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("9008", (await changed.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Postcode);
    }

    // The profile chose 3089 while it was active; the table has since retired it. S102.3's distance
    // clause becomes observable with cards (task 18).
    [Fact]
    [Trait("SpecPartial", "S102")]
    [Trait("SpecPartial", "S102.3")]
    public async Task Retired_postcode_kept_when_other_fields_change()
    {
        Assert.True(FixturePostcodes.Get(FixturePostcodes.Retired).Retired);
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account, "0150"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        await ExecuteAsync(
            "UPDATE profiles SET postcode = @postcode",
            ("postcode", Factory.Data.Postcode(FixturePostcodes.Retired)));
        var changed = Factory.Data.Profile(account, FixturePostcodes.Retired) with { OwnGear = false, MaxTravelKm = 60 };

        using var saved = await PutAsync(token, changed);
        using var read = await GetAsync(token);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AssertProfile(Expected(changed), await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct));
    }

    [Theory]
    [Trait("Spec", "S61")]
    [InlineData("tuesday", 18, 21, "tuesday", 20, 22)] // partly
    [InlineData("tuesday", 18, 21, "tuesday", 18, 21)] // the same window twice
    [InlineData("tuesday", 18, 22, "tuesday", 19, 20)] // one inside the other
    [InlineData("sunday", 0, 24, "sunday", 23, 24)]
    public async Task Overlapping_windows_rejected(string firstDay, int firstStart, int firstEnd, string secondDay, int secondStart, int secondEnd)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with
        {
            Windows =
            [
                new WindowRequest("monday", 9, 11),
                new WindowRequest(firstDay, firstStart, firstEnd),
                new WindowRequest(secondDay, secondStart, secondEnd),
            ],
        };

        using var response = await PutAsync(token, request);
        using var read = await GetAsync(token);

        await AssertRejectedAsync(response, AvailabilityWindow.OverlapCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    [Trait("Spec", "S61")]
    public async Task Overlapping_windows_rejected_on_update_and_the_stored_ones_kept()
    {
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account));

        using var response = await PutAsync(token, Factory.Data.Profile(account) with
        {
            Windows = [new WindowRequest("friday", 17, 20), new WindowRequest("friday", 19, 23)],
        });
        using var read = await GetAsync(token);

        await AssertRejectedAsync(response, AvailabilityWindow.OverlapCode);
        Assert.Equal([new WindowResponse("tuesday", 18, 21)], (await read.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Windows);
    }

    [Fact]
    [Trait("Spec", "S61")]
    public async Task Windows_that_touch_or_lie_on_other_days_are_accepted()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with
        {
            Windows =
            [
                new WindowRequest("wednesday", 20, 22),
                new WindowRequest("wednesday", 18, 20),
                new WindowRequest("thursday", 18, 22),
                new WindowRequest("sunday", 0, 24),
            ],
        };

        using var saved = await PutAsync(token, request);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(
            [
                new WindowResponse("sunday", 0, 24),
                new WindowResponse("wednesday", 18, 20),
                new WindowResponse("wednesday", 20, 22),
                new WindowResponse("thursday", 18, 22),
            ],
            (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Windows);
    }

    [Theory]
    [Trait("Spec", "S61")]
    [InlineData("tuesday", 20, 20)] // ends when it starts
    [InlineData("tuesday", 22, 18)] // ends before it starts
    [InlineData("tuesday", 22, 25)] // ends the next day
    [InlineData("tuesday", -1, 2)]
    [InlineData("Tuesday", 18, 20)]
    [InlineData("tirsdag", 18, 20)]
    [InlineData(null, 18, 20)]
    public async Task Window_that_does_not_end_later_the_same_day_is_rejected(string? day, int start, int end)
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var response = await PutAsync(token, Factory.Data.Profile(account) with { Windows = [new WindowRequest(day, start, end)] });

        await AssertRejectedAsync(response, AvailabilityWindow.InvalidCode);
    }

    [Theory]
    [Trait("SpecPartial", "S3")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task At_least_one_window_is_required(bool empty)
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var response = await PutAsync(token, Factory.Data.Profile(account) with { Windows = empty ? [] : null });

        await AssertRejectedAsync(response, AvailabilityWindow.RequiredCode);
    }

    [Fact]
    [Trait("SpecPartial", "S18")]
    public async Task Every_active_gym_can_be_a_home_gym()
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var saved = await PutAsync(token, Factory.Data.Profile(account) with { HomeGyms = [.. FixtureGyms.Active.Select(gym => gym.Id)] });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(FixtureGyms.Active.Select(gym => gym.Id), (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.HomeGyms);
    }

    // S18 stays partial: gyms exist only as fixtures until the admin flow (S17, M2).
    [Fact]
    [Trait("SpecPartial", "S18")]
    public async Task Inactive_gym_rejected()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var request = Factory.Data.Profile(account) with { HomeGyms = [FixtureGyms.Active[0].Id, FixtureGyms.Inactive.Id] };

        using var response = await PutAsync(token, request);
        using var read = await GetAsync(token);

        await AssertRejectedAsync(response, SaveProfile.GymInactiveCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S18")]
    public async Task Gym_that_does_not_exist_is_rejected()
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var response = await PutAsync(token, Factory.Data.Profile(account) with { HomeGyms = [Guid.NewGuid()] });

        await AssertRejectedAsync(response, SaveProfile.GymUnknownCode);
    }

    // S21 (M2): the gym was active when the profile listed it, and has been deactivated since.
    [Fact]
    [Trait("SpecPartial", "S18")]
    [Trait("SpecPartial", "S21")]
    public async Task Gym_listed_before_it_was_deactivated_is_kept()
    {
        var (account, token) = await RegisterAndLogInAsync();
        using var created = await PutAsync(token, Factory.Data.Profile(account));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        await ExecuteAsync(
            "INSERT INTO home_gyms (account_id, gym_id, position) SELECT account_id, @gym, 1 FROM profiles",
            ("gym", FixtureGyms.Inactive.Id));
        var request = Factory.Data.Profile(account) with { HomeGyms = [FixtureGyms.Inactive.Id, FixtureGyms.Active[0].Id] };

        using var saved = await PutAsync(token, request);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(request.HomeGyms, (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.HomeGyms);
    }

    public static TheoryData<string, string> InvalidFields => new()
    {
        { "display-name-empty", Profile.DisplayNameInvalidCode },
        { "display-name-blank", Profile.DisplayNameInvalidCode },
        { "display-name-51", Profile.DisplayNameInvalidCode },
        { "display-name-control", Profile.DisplayNameInvalidCode },
        { "display-name-invisible", Profile.DisplayNameInvalidCode },
        { "display-name-accents-51", Profile.DisplayNameInvalidCode },
        { "sex-missing", Profile.SexInvalidCode },
        { "sex-unknown", Profile.SexInvalidCode },
        { "accepted-sexes-empty", Profile.AcceptedSexesInvalidCode },
        { "accepted-sexes-unknown", Profile.AcceptedSexesInvalidCode },
        { "discipline-unknown", Profile.VariantInvalidCode },
        { "multipitch-indoor", Profile.VariantInvalidCode },
        { "ice-indoor", Profile.VariantInvalidCode },
        { "venues-empty", Profile.VariantInvalidCode },
        { "venue-twice", Profile.VariantInvalidCode },
        { "variant-in-two-entries", Profile.VariantDuplicateCode },
        { "range-missing", Profile.GradeInvalidCode },
        { "range-reversed", Profile.GradeRangeInvalidCode },
        { "partner-range-reversed", Profile.GradeRangeInvalidCode },
        { "certification-unknown", Profile.CertificationInvalidCode },
        { "own-gear-missing", Profile.OwnGearRequiredCode },
        { "travel-0", Profile.TravelDistanceInvalidCode },
        { "travel-101", Profile.TravelDistanceInvalidCode },
        { "travel-missing", Profile.TravelDistanceInvalidCode },
    };

    [Theory]
    [Trait("SpecPartial", "S3")]
    [MemberData(nameof(InvalidFields))]
    public async Task Invalid_field_is_rejected_with_its_code(string field, string code)
    {
        var (account, token) = await RegisterAndLogInAsync();
        var valid = Factory.Data.Profile(account);
        var grades = new GradeRangeRequest("5", "6A");
        var request = field switch
        {
            "display-name-empty" => valid with { DisplayName = "" },
            "display-name-blank" => valid with { DisplayName = "   " },
            "display-name-51" => valid with { DisplayName = new string('k', 51) },
            "display-name-control" => valid with { DisplayName = "Klatrer\u0007" },
            "display-name-invisible" => valid with { DisplayName = "\u200B" },
            "display-name-accents-51" => valid with { DisplayName = "k" + new string('\u0301', 51) },
            "sex-missing" => valid with { Sex = null },
            "sex-unknown" => valid with { Sex = "kvinne" },
            "accepted-sexes-empty" => valid with { AcceptedSexes = [] },
            "accepted-sexes-unknown" => valid with { AcceptedSexes = ["female", "any"] },
            "discipline-unknown" => valid with { Disciplines = [new DisciplineEntryRequest("sport", ["indoor"], grades, grades)] },
            "multipitch-indoor" => valid with { Disciplines = [Rope("multipitch", "indoor")] },
            "ice-indoor" => valid with { Disciplines = [new DisciplineEntryRequest("ice", ["indoor"], Ice, Ice)] },
            "venues-empty" => valid with { Disciplines = [new DisciplineEntryRequest("bouldering", [], grades, grades)] },
            "venue-twice" => valid with { Disciplines = [new DisciplineEntryRequest("bouldering", ["indoor", "indoor"], grades, grades)] },
            "variant-in-two-entries" => valid with { Disciplines = [Rope("lead", "indoor", "outdoor"), Rope("lead", "outdoor")] },
            "range-missing" => valid with { Disciplines = [new DisciplineEntryRequest("bouldering", ["indoor"], null, grades)] },
            "range-reversed" => valid with { Disciplines = [new DisciplineEntryRequest("bouldering", ["indoor"], new GradeRangeRequest("6A", "5"), grades)] },
            "partner-range-reversed" => valid with { Disciplines = [new DisciplineEntryRequest("bouldering", ["indoor"], grades, new GradeRangeRequest("7A", "6C+"))] },
            "certification-unknown" => valid with { Certification = "brattkort" },
            "own-gear-missing" => valid with { OwnGear = null },
            "travel-0" => valid with { MaxTravelKm = 0 },
            "travel-101" => valid with { MaxTravelKm = 101 },
            "travel-missing" => valid with { MaxTravelKm = null },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        using var response = await PutAsync(token, request);

        await AssertRejectedAsync(response, code);
    }

    [Theory]
    [Trait("SpecPartial", "S3")]
    [InlineData("  Kari Nordmann  ", "Kari Nordmann")]
    [InlineData("Åse Ødegård-Lie", "Åse Ødegård-Lie")]
    public async Task Display_name_is_trimmed_and_kept_as_written(string written, string stored)
    {
        var (account, token) = await RegisterAndLogInAsync();
        Factory.Fixtures.Register("display-name", written.Trim());

        using var saved = await PutAsync(token, Factory.Data.Profile(account) with { DisplayName = written });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(stored, (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.DisplayName);
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Display_name_of_50_characters_is_accepted()
    {
        var (account, token) = await RegisterAndLogInAsync();
        var name = new string('k', Profile.MaximumDisplayNameLength);
        Factory.Fixtures.Register("display-name", name);

        using var saved = await PutAsync(token, Factory.Data.Profile(account) with { DisplayName = name });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    [Theory]
    [Trait("SpecPartial", "S3")]
    [InlineData(Profile.MinimumTravelKm)]
    [InlineData(Profile.MaximumTravelKm)]
    public async Task Travel_distance_bounds_are_accepted(int km)
    {
        var (account, token) = await RegisterAndLogInAsync();

        using var saved = await PutAsync(token, Factory.Data.Profile(account) with { MaxTravelKm = km });

        Assert.Equal(km, (await saved.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.MaxTravelKm);
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Climber_without_a_profile_reads_not_found()
    {
        var (_, token) = await RegisterAndLogInAsync();

        using var read = await GetAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(ErrorCodes.NotFound, (await read.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code);
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Each_climber_reads_only_their_own_profile()
    {
        var (first, firstToken) = await RegisterAndLogInAsync();
        var (_, secondToken) = await RegisterAndLogInAsync();
        using var saved = await PutAsync(firstToken, Factory.Data.Profile(first));

        using var read = await GetAsync(secondToken);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    [Trait("SpecPartial", "S3")]
    public async Task Only_a_logged_in_climber_has_a_profile()
    {
        var account = Factory.Data.Account();

        using var read = await GetAsync(token: null);
        using var save = await PutAsync(token: null, Factory.Data.Profile(account));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
        Assert.Equal(ClimberSessions.RequiredCode, (await save.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code);
    }

    // C46 checks the two files agree; this checks the codes are among their keys.
    [Fact]
    public void Every_profile_error_code_has_a_text_in_both_languages() =>
        Assert.Empty(Profile.Codes.Concat(SaveProfile.Codes).SelectMany(code => new[] { Language.English, Language.Bokmal }
            .Where(language => Texts.Get(code, language) == code)
            .Select(language => $"{code} ({language})")));

    private static GradeRangeRequest Ice => new("WI3", "WI4");

    private static DisciplineEntryRequest Rope(string discipline, params string[] venues) =>
        new(discipline, venues, new GradeRangeRequest("6", "7"), new GradeRangeRequest("6", "7"));

    // The response a valid request is saved as: sexes, venues, entries (by first variant) and windows in order,
    // gyms once each.
    private static ProfileResponse Expected(ProfileRequest request) => new(
        request.DisplayName!.Trim(),
        request.Sex!,
        request.AcceptedSexes is null ? ["female", "male", "other"] : [.. request.AcceptedSexes.Distinct().Order(StringComparer.Ordinal).Select(sex => sex!)],
        request.RopeScale!,
        [.. request.Disciplines!.OrderBy(entry => DisciplineVariants.Of(
                WireNames.Parse<Discipline>(entry!.Discipline)!.Value,
                entry.Venues!.Select(venue => WireNames.Parse<Venue>(venue)!.Value).Min())).Select(entry => new DisciplineEntryResponse(
            entry!.Discipline!,
            [.. entry.Venues!.Order(StringComparer.Ordinal).Select(venue => venue!)],
            new GradeRangeResponse(entry.Own!.Lowest!, entry.Own.Highest!),
            new GradeRangeResponse(entry.Partner!.Lowest!, entry.Partner.Highest!)))],
        request.Certification!,
        request.OwnGear!.Value,
        request.Postcode!,
        request.MaxTravelKm!.Value,
        [.. request.HomeGyms!.Distinct()],
        [.. request.Windows!.Select(window => new WindowResponse(window!.Day!, window.Start!.Value, window.End!.Value))
            .OrderBy(window => Enum.Parse<DayOfWeek>(window.Day, ignoreCase: true)).ThenBy(window => window.Start)]);

    // Records hold lists, which compare by reference; this compares them by content.
    private static void AssertProfile(ProfileResponse expected, ProfileResponse? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(
            expected with { AcceptedSexes = [], Disciplines = [], HomeGyms = [], Windows = [] },
            actual with { AcceptedSexes = [], Disciplines = [], HomeGyms = [], Windows = [] });
        Assert.Equal(expected.AcceptedSexes, actual.AcceptedSexes);
        Assert.Equal(expected.HomeGyms, actual.HomeGyms);
        Assert.Equal(expected.Windows, actual.Windows);
        Assert.Equal(Describe(expected.Disciplines), Describe(actual.Disciplines));
    }

    private static List<string> Describe(IEnumerable<DisciplineEntryResponse> entries) =>
        [.. entries.Select(entry => $"{entry.Discipline} {string.Join('+', entry.Venues)} {entry.Own} {entry.Partner}")];

    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task<(AccountDraft Account, string Token)> RegisterAndLogInAsync()
    {
        var account = Factory.Data.Account();
        using var registration = await Client().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, "nb", TermsVersion: 1, PrivacyVersion: 1),
            Ct);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var login = await Client().PostAsJsonAsync("/api/climber/login", new LoginRequest(account.Email, account.Password), Ct);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var token = SetCookieHeaderValue.ParseList([.. login.Headers.GetValues(HeaderNames.SetCookie)])
            .Single(cookie => cookie.Name == SessionCookie.Name).Value.Value!;
        Factory.Fixtures.Register("session-token", token);
        return (account, token);
    }

    private Task<HttpResponseMessage> GetAsync(string? token) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Route), token);

    private Task<HttpResponseMessage> PutAsync(string? token, ProfileRequest request) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(request) }, token);

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? token)
    {
        using (request)
        {
            if (token is not null)
            {
                request.Headers.Add(HeaderNames.Cookie, $"{SessionCookie.Name}={token}");
            }

            return await Client().SendAsync(request, Ct);
        }
    }

    // One value per discipline_variants row of the account, ordered by variant.
    private async Task<List<string>> VariantColumnAsync(AccountDraft account, string expression)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {expression} FROM discipline_variants v JOIN accounts a ON a.id = v.account_id
            WHERE a.email = @email ORDER BY v.variant
            """,
            connection);
        command.Parameters.AddWithValue("email", account.Email);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        Assert.Equal(1, await command.ExecuteNonQueryAsync(Ct));
    }
}
