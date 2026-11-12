using System.Net;
using System.Text.Json;
using Bunit;
using ClimbOn.Client.Pages;
using ClimbOn.Client.Services;
using Xunit;

namespace ClimbOn.Client.Tests;

// The profile editor sends what was entered to the api and shows what it answers. No profile rule is checked in
// the client (C7): the api's S3, S15 and S61 checks are the only ones.
public sealed class ProfilePageTests : ClientTestContext
{
    private const string ProfileRoute = "/api/climber/profile";

    private static readonly Guid Gym = Guid.Parse("6f1d8f3c-2a4b-4c5d-9e6f-7a8b9c0d1e2f");

    [Fact]
    public void Save_puts_profile_and_shows_server_error()
    {
        Api.Answer(HttpMethod.Get, ProfileRoute, HttpStatusCode.OK, Stored())
            .Fail(HttpMethod.Put, ProfileRoute, HttpStatusCode.BadRequest, "display_name_invalid", "Server text.");
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));

        page.Find("input[autocomplete=nickname]").Change("Kari K");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("display_name_invalid", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        Assert.Empty(page.FindAll(".success"));

        // The stored profile goes back as it was, with the one change, home gyms included.
        var request = Api.Single(HttpMethod.Put, ProfileRoute).Json;
        Assert.Equal("Kari K", request.GetProperty("displayName").GetString());
        Assert.Equal("female", request.GetProperty("sex").GetString());
        Assert.Equal(["female", "other"], Strings(request.GetProperty("acceptedSexes")));
        Assert.Equal("french", request.GetProperty("ropeScale").GetString());
        var discipline = Assert.Single(request.GetProperty("disciplines").EnumerateArray());
        Assert.Equal("lead", discipline.GetProperty("discipline").GetString());
        Assert.Equal(["indoor", "outdoor"], Strings(discipline.GetProperty("venues")));
        Assert.Equal("6a", discipline.GetProperty("own").GetProperty("lowest").GetString());
        Assert.Equal("7a+", discipline.GetProperty("own").GetProperty("highest").GetString());
        Assert.Equal("6b", discipline.GetProperty("partner").GetProperty("lowest").GetString());
        Assert.Equal("7b", discipline.GetProperty("partner").GetProperty("highest").GetString());
        Assert.Equal("brattkort-led", request.GetProperty("certification").GetString());
        Assert.True(request.GetProperty("ownGear").GetBoolean());
        Assert.Equal("0150", request.GetProperty("postcode").GetString());
        Assert.Equal(25, request.GetProperty("maxTravelKm").GetInt32());
        Assert.Equal([Gym.ToString()], Strings(request.GetProperty("homeGyms")));
        var window = Assert.Single(request.GetProperty("windows").EnumerateArray());
        Assert.Equal("tuesday", window.GetProperty("day").GetString());
        Assert.Equal(18, window.GetProperty("start").GetInt32());
        Assert.Equal(21, window.GetProperty("end").GetInt32());

        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Empty(MissingTexts);
    }

    // S61's overlap is the api's rule: two overlapping windows are sent as entered, and the api's answer shown.
    [Fact]
    public void No_client_side_rule_validation()
    {
        Api.Fail(HttpMethod.Put, ProfileRoute, HttpStatusCode.BadRequest, "windows_overlap", "Server text.");
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));

        page.Find("button.add-window").Click();
        page.Find("button.add-window").Click();
        EnterWindow(page, 0, "monday", "10", "14");
        EnterWindow(page, 1, "monday", "12", "16");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("windows_overlap", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        var sent = Api.Single(HttpMethod.Put, ProfileRoute).Json.GetProperty("windows").EnumerateArray()
            .Select(window => (window.GetProperty("day").GetString(), window.GetProperty("start").GetInt32(), window.GetProperty("end").GetInt32()))
            .ToList();
        Assert.Equal([("monday", 10, 14), ("monday", 12, 16)], sent);

        // Nothing the browser could refuse a submit over.
        Assert.True(page.Find("form").HasAttribute("novalidate"));
        Assert.Empty(page.FindAll("[required], [min], [max], [pattern], [maxlength], [minlength]"));
        Assert.Empty(MissingTexts);
    }

    // Removing an entry is how a climber answers a refusal such as S61's windows_overlap: the removed entry is not
    // sent, and the others are.
    [Fact]
    public void Removed_discipline_and_window_are_not_sent()
    {
        Api.Answer(HttpMethod.Get, ProfileRoute, HttpStatusCode.OK, Stored())
            .Answer(HttpMethod.Put, ProfileRoute, HttpStatusCode.OK, Stored());
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));

        page.Find("button.add-discipline").Click();
        page.FindAll(".discipline-entry select")[1].Change("bouldering");
        page.Find("button.add-window").Click();
        EnterWindow(page, 1, "monday", "10", "14");
        page.FindAll(".discipline-entry button.remove")[0].Click();
        page.FindAll(".window-entry button.remove")[0].Click();
        Assert.Single(page.FindAll(".discipline-entry"));
        Assert.Single(page.FindAll(".window-entry"));
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        var request = Api.Single(HttpMethod.Put, ProfileRoute).Json;
        var discipline = Assert.Single(request.GetProperty("disciplines").EnumerateArray());
        Assert.Equal("bouldering", discipline.GetProperty("discipline").GetString());
        var window = Assert.Single(request.GetProperty("windows").EnumerateArray());
        Assert.Equal("monday", window.GetProperty("day").GetString());
        Assert.Equal(10, window.GetProperty("start").GetInt32());
    }

    // No profile yet (404): an empty form, sent empty; every required value is the api's to ask for.
    [Fact]
    public void New_profile_is_sent_as_entered()
    {
        Api.Fail(HttpMethod.Get, ProfileRoute, HttpStatusCode.NotFound, "not_found", "Not found.")
            .Fail(HttpMethod.Put, ProfileRoute, HttpStatusCode.BadRequest, "display_name_invalid", "Server text.");
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));
        Assert.Empty(page.FindAll("[role=alert]"));

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("display_name_invalid", page.Find("[role=alert]").GetAttribute("data-code")));
        var request = Api.Single(HttpMethod.Put, ProfileRoute);
        Assert.Null(request.StringProperty("displayName"));
        Assert.Null(request.StringProperty("postcode"));
        Assert.Equal(JsonValueKind.Null, Kind(request.Json, "ownGear"));
        Assert.Equal(JsonValueKind.Null, Kind(request.Json, "maxTravelKm"));

        // Every sex is accepted until the climber says otherwise (S3's default).
        Assert.Equal(["female", "male", "other"], Strings(request.Json.GetProperty("acceptedSexes")));
        Assert.Equal(3, page.FindAll("input[name=accepted-sex][checked]").Count);
    }

    [Fact]
    public void Entered_discipline_and_choices_are_sent()
    {
        Api.Answer(HttpMethod.Put, ProfileRoute, HttpStatusCode.OK, Stored());
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));

        page.Find("button.add-discipline").Click();
        page.Find(".discipline-entry select").Change("bouldering");
        page.Find(".discipline-entry input[name=venue][value=indoor]").Change(true);
        page.Find(".discipline-entry input[name=own-lowest]").Change("6A");
        page.Find(".discipline-entry input[name=own-highest]").Change("7A");
        page.Find(".discipline-entry input[name=partner-lowest]").Change("6B");
        page.Find(".discipline-entry input[name=partner-highest]").Change("7B");
        page.Find("input[name=accepted-sex][value=male]").Change(false);
        page.Find("select[name=own-gear]").Change("no");
        page.Find("input[name=postcode]").Change("7010");
        page.Find("input[name=max-travel-km]").Change("40");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        var request = Api.Single(HttpMethod.Put, ProfileRoute).Json;
        var discipline = Assert.Single(request.GetProperty("disciplines").EnumerateArray());
        Assert.Equal("bouldering", discipline.GetProperty("discipline").GetString());
        Assert.Equal(["indoor"], Strings(discipline.GetProperty("venues")));
        Assert.Equal("6A", discipline.GetProperty("own").GetProperty("lowest").GetString());
        Assert.Equal("7B", discipline.GetProperty("partner").GetProperty("highest").GetString());
        Assert.Equal(["female", "other"], Strings(request.GetProperty("acceptedSexes")));
        Assert.False(request.GetProperty("ownGear").GetBoolean());
        Assert.Equal("7010", request.GetProperty("postcode").GetString());
        Assert.Equal(40, request.GetProperty("maxTravelKm").GetInt32());

        // The saved profile is the api's answer.
        Assert.Equal("Kari", page.Find("input[autocomplete=nickname]").GetAttribute("value"));
        Assert.Empty(MissingTexts);
    }

    // Own gear (S3) is shown as stored, and either choice is sent as the climber made it.
    [Fact]
    public void Own_gear_shows_the_stored_value_and_sends_the_choice()
    {
        Api.Answer(HttpMethod.Get, ProfileRoute, HttpStatusCode.OK, Stored())
            .Answer(HttpMethod.Put, ProfileRoute, HttpStatusCode.OK, Stored());
        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));
        Assert.Equal("yes", page.Find("select[name=own-gear]").GetAttribute("value"));

        page.Find("select[name=own-gear]").Change("no");
        Assert.Equal("no", page.Find("select[name=own-gear]").GetAttribute("value"));
        page.Find("select[name=own-gear]").Change("yes");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        Assert.True(Api.Single(HttpMethod.Put, ProfileRoute).Json.GetProperty("ownGear").GetBoolean());
    }

    // A stored profile today's rules refuse is the climber's to replace: the api's refusal is shown over an empty
    // form, and saving it puts the replacement.
    [Fact]
    public void Refused_stored_profile_offers_an_empty_form_to_replace_it()
    {
        Api.Fail(HttpMethod.Get, ProfileRoute, HttpStatusCode.BadRequest, "display_name_invalid", "Server text.")
            .Answer(HttpMethod.Put, ProfileRoute, HttpStatusCode.OK, Stored());
        var page = Render<Profile>();

        page.WaitForAssertion(() => Assert.Equal("display_name_invalid", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        Assert.Null(page.Find("input[autocomplete=nickname]").GetAttribute("value"));

        page.Find("input[autocomplete=nickname]").Change("Kari");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(page.Find(".success")));
        Assert.Equal("Kari", Api.Single(HttpMethod.Put, ProfileRoute).StringProperty("displayName"));
        Assert.Empty(page.FindAll("[role=alert]"));
        Assert.Empty(MissingTexts);
    }

    // Not logged in, or any refusal other than the profile's own: the message only.
    [Fact]
    public void Unauthorized_load_shows_message_and_no_form()
    {
        Api.Fail(HttpMethod.Get, ProfileRoute, HttpStatusCode.Unauthorized, "session_required", "Server text.");

        var page = Render<Profile>();

        page.WaitForAssertion(() => Assert.Equal("session_required", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void Load_failure_shows_message_and_no_form()
    {
        Api.Unreachable(HttpMethod.Get, ProfileRoute);

        var page = Render<Profile>();

        page.WaitForAssertion(() => Assert.Equal(Text(ServerMessage.NetworkKey), page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(MissingTexts);
    }

    [Fact]
    public void Shows_every_text_in_bokmal()
    {
        UseLanguage(UiLanguage.Bokmal);
        Api.Answer(HttpMethod.Get, ProfileRoute, HttpStatusCode.OK, Stored());

        var page = Render<Profile>();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("form")));
        page.Find("button.add-discipline").Click();
        page.Find("button.add-window").Click();

        Assert.Empty(MissingTexts);
    }

    // Each field found afresh: a change re-renders the form.
    private static void EnterWindow(IRenderedComponent<Profile> page, int index, string day, string start, string end)
    {
        page.FindAll(".window-entry select[name=day]")[index].Change(day);
        page.FindAll(".window-entry input[name=start]")[index].Change(start);
        page.FindAll(".window-entry input[name=end]")[index].Change(end);
    }

    private static List<string?> Strings(JsonElement array) => [.. array.EnumerateArray().Select(value => value.GetString())];

    private static JsonValueKind Kind(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) ? value.ValueKind : JsonValueKind.Null;

    private static object Stored() => new
    {
        displayName = "Kari",
        sex = "female",
        acceptedSexes = new[] { "female", "other" },
        ropeScale = "french",
        disciplines = new[]
        {
            new
            {
                discipline = "lead",
                venues = new[] { "indoor", "outdoor" },
                own = new { lowest = "6a", highest = "7a+" },
                partner = new { lowest = "6b", highest = "7b" },
            },
        },
        certification = "brattkort-led",
        ownGear = true,
        postcode = "0150",
        maxTravelKm = 25,
        homeGyms = new[] { Gym },
        windows = new[] { new { day = "tuesday", start = 18, end = 21 } },
    };
}
