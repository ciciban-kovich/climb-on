using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClimbOn.Api.Climber;
using ClimbOn.Api.Errors;
using ClimbOn.Api.Middleware;
using ClimbOn.Api.Resources;
using ClimbOn.Application.Cards;
using ClimbOn.Application.Matching;
using ClimbOn.Domain.Accounts;
using ClimbOn.Domain.Geo;
using ClimbOn.Integration.Tests.Harness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Npgsql;
using Xunit;

namespace ClimbOn.Integration.Tests.Matching;

// The suggestion list through GET /api/climber/suggestions (design §2 'Matching'). Every climber is built by
// varying TestData.Profile, so two of them match unless a test changes that.
public sealed class SuggestionListTests(ApiFactory factory) : IntegrationTest(factory)
{
    private const string Route = "/api/climber/suggestions";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Spec", "S4")]
    [Trait("SpecPartial", "S54")]
    public async Task Climbers_meeting_every_criterion_see_each_other()
    {
        var a = await ClimberAsync();
        var b = await ClimberAsync();

        Assert.Equal([b.Name], Names(await ListAsync(a)));
        Assert.Equal([a.Name], Names(await ListAsync(b)));
    }

    [Fact]
    [Trait("Spec", "S4")]
    public async Task A_climber_is_not_their_own_candidate()
    {
        var a = await ClimberAsync();

        Assert.Empty(await ListAsync(a));
    }

    // Each pair is a little past a whole km apart, which rounds down to it (S52). 0150 and 0250 are 1.13 km apart,
    // mostly east-west, so the prefilter's box must reach past 1 km at its edge for the pair to be listed.
    [Theory]
    [Trait("Spec", "S4.1")]
    [Trait("Spec", "S16")]
    [Trait("SpecPartial", "S52")]
    [InlineData("0560", "1300", 14, 100, true)]
    [InlineData("0560", "1300", 100, 14, true)]
    [InlineData("0560", "1300", 13, 100, false)]
    [InlineData("0560", "1300", 100, 13, false)]
    [InlineData("0150", "0250", 1, 100, true)]
    [InlineData("0150", "0250", 100, 1, true)]
    public async Task Edge_distance_is_included(string aPostcode, string bPostcode, int aMaxKm, int bMaxKm, bool listed)
    {
        var km = Distance.Kilometres(Point(aPostcode), Point(bPostcode));
        Assert.InRange(km - Math.Floor(km), 0.1, 0.4);
        Assert.Equal(listed, Math.Floor(km) <= Math.Min(aMaxKm, bMaxKm));

        var a = await ClimberAsync(aPostcode, profile => profile with { MaxTravelKm = aMaxKm });
        var b = await ClimberAsync(bPostcode, profile => profile with { MaxTravelKm = bMaxKm });

        Assert.Equal(listed ? [b.Name] : [], Names(await ListAsync(a)));
        Assert.Equal(listed ? [a.Name] : [], Names(await ListAsync(b)));
    }

    [Theory]
    [Trait("Spec", "S4.2")]
    [InlineData("female", null, "male", null, true)]
    [InlineData("female", new[] { "male" }, "male", new[] { "female" }, true)]
    [InlineData("other", new[] { "other" }, "other", new[] { "other" }, true)]
    [InlineData("female", new[] { "female" }, "male", null, false)]
    [InlineData("female", null, "male", new[] { "male", "other" }, false)]
    public async Task Each_accepts_the_others_sex(string aSex, string[]? aAccepts, string bSex, string[]? bAccepts, bool listed)
    {
        var a = await ClimberAsync(change: profile => profile with { Sex = aSex, AcceptedSexes = aAccepts });
        var b = await ClimberAsync(change: profile => profile with { Sex = bSex, AcceptedSexes = bAccepts });

        Assert.Equal(listed ? [b.Name] : [], Names(await ListAsync(a)));
        Assert.Equal(listed ? [a.Name] : [], Names(await ListAsync(b)));
    }

    // S12 per variant: both climbers have only this variant, with these certifications.
    [Theory]
    [Trait("Spec", "S4.3")]
    [Trait("Spec", "S12")]
    [InlineData("top-rope", "indoor", "brattkort-topptau", "brattkort-led", true)]
    [InlineData("top-rope", "indoor", "brattkort-topptau", "none", false)]
    [InlineData("lead", "indoor", "brattkort-led", "brattkort-led", true)]
    [InlineData("lead", "indoor", "brattkort-led", "brattkort-topptau", false)]
    [InlineData("bouldering", "indoor", "none", "none", true)]
    [InlineData("bouldering", "outdoor", "none", "none", true)]
    [InlineData("top-rope", "outdoor", "none", "none", true)]
    [InlineData("lead", "outdoor", "none", "none", true)]
    [InlineData("multipitch", "outdoor", "none", "none", true)]
    [InlineData("ice", "outdoor", "none", "none", true)]
    public async Task Shared_variant_needs_both_to_meet_its_certification(
        string discipline, string venue, string aCertification, string bCertification, bool listed)
    {
        var entry = Entry(discipline, venue);
        var a = await ClimberAsync(change: profile => profile with { Disciplines = [entry], Certification = aCertification });
        var b = await ClimberAsync(change: profile => profile with { Disciplines = [entry], Certification = bCertification });

        Assert.Equal(listed ? [b.Name] : [], Names(await ListAsync(a)));
        Assert.Equal(listed ? [a.Name] : [], Names(await ListAsync(b)));
    }

    [Fact]
    [Trait("Spec", "S4.3")]
    public async Task Variants_of_one_discipline_at_different_venues_are_not_shared()
    {
        var a = await ClimberAsync(change: profile => profile with { Disciplines = [Entry("bouldering", "indoor")] });
        var b = await ClimberAsync(change: profile => profile with { Disciplines = [Entry("bouldering", "outdoor")] });

        Assert.Empty(await ListAsync(a));
        Assert.Empty(await ListAsync(b));
    }

    // Grades far apart in both directions (grade fit 0), and changed again, never move a climber in or out.
    [Fact]
    [Trait("Spec", "S14")]
    public async Task Changing_grades_never_changes_membership()
    {
        var a = await ClimberAsync();
        var listed = await ClimberAsync();
        var hidden = await ClimberAsync(change: profile => profile with { AcceptedSexes = ["male"] });
        Assert.Equal([listed.Name], Names(await ListAsync(a)));

        foreach (var (own, partner) in new[] { (("8A", "9A"), ("3", "4")), (("3", "4"), ("8A", "9A")) })
        {
            var regraded = new DisciplineEntryRequest(
                "bouldering", ["indoor"], new GradeRangeRequest(own.Item1, own.Item2), new GradeRangeRequest(partner.Item1, partner.Item2));
            await SaveProfileAsync(listed, profile => profile with { Disciplines = [regraded] });
            await SaveProfileAsync(hidden, profile => profile with { AcceptedSexes = ["male"], Disciplines = [regraded] });

            Assert.Equal([listed.Name], Names(await ListAsync(a)));
        }
    }

    [Fact]
    [Trait("SpecPartial", "S116")]
    public async Task Unconfirmed_climber_appears_in_no_list_until_confirmed()
    {
        var a = await ClimberAsync();
        var b = await ClimberAsync(confirm: false);

        Assert.Empty(await ListAsync(a));

        await ConfirmAsync(b.Account);
        Assert.Equal([b.Name], Names(await ListAsync(a)));
    }

    [Fact]
    [Trait("SpecPartial", "S116")]
    public async Task Unconfirmed_requester_is_refused()
    {
        var a = await ClimberAsync(confirm: false);

        using var response = await GetAsync(a.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(GetSuggestions.EmailNotConfirmedCode, (await response.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code);
    }

    [Fact]
    public async Task Only_a_logged_in_climber_has_a_list()
    {
        using var response = await GetAsync(token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ClimberSessions.RequiredCode, (await response.Content.ReadFromJsonAsync<ErrorBody>(Ct))?.Code);
    }

    [Fact]
    [Trait("Spec", "S4")]
    public async Task Climber_without_a_profile_has_an_empty_list()
    {
        await ClimberAsync();
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        await ConfirmAsync(account);

        using var response = await GetAsync(await LogInAsync(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<SuggestionListResponse>(Ct))!.Candidates);
    }

    // Dormant is a last activity more than 60 days ago (specs glossary); sessions last 30 days (S82), so the
    // requester logs in again each time.
    [Fact]
    [Trait("SpecPartial", "S107")]
    public async Task Dormant_climber_hidden_until_active()
    {
        var a = await ClimberAsync();
        var b = await ClimberAsync();

        Factory.Clock.Advance(DormancyPolicy.After);
        Assert.Equal([b.Name], Names(await ListAsync(a with { Token = await LogInAsync(a.Account) })));

        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var later = a with { Token = await LogInAsync(a.Account) };
        Assert.Empty(await ListAsync(later));

        await LogInAsync(b.Account);
        Assert.Equal([b.Name], Names(await ListAsync(later)));
    }

    [Fact]
    [Trait("SpecPartial", "S10")]
    public async Task Lists_are_symmetric()
    {
        var population = new List<TestClimber>
        {
            await ClimberAsync("0150"),
            await ClimberAsync("0560", profile => profile with { MaxTravelKm = 5 }),
            await ClimberAsync("1300", profile => profile with { MaxTravelKm = 14 }),
            await ClimberAsync("2000", profile => profile with { MaxTravelKm = 100 }),
            await ClimberAsync("0250", profile => profile with { Sex = "male", AcceptedSexes = ["female"] }),
            await ClimberAsync("0355", profile => profile with { AcceptedSexes = ["male"] }),
            await ClimberAsync("0187", profile => profile with { Disciplines = [Entry("ice", "outdoor")] }),
            await ClimberAsync("0661", profile => profile with { Disciplines = [Entry("lead", "indoor")], Certification = "brattkort-topptau" }),
            await ClimberAsync("5003", profile => profile with { MaxTravelKm = 100 }),
        };

        var lists = new Dictionary<string, List<string>>();
        foreach (var climber in population)
        {
            lists[climber.Name] = Names(await ListAsync(climber));
        }

        var pairs = population.SelectMany(x => population.Where(y => y != x).Select(y => (X: x.Name, Y: y.Name))).ToList();
        Assert.DoesNotContain(pairs, pair => lists[pair.X].Contains(pair.Y) != lists[pair.Y].Contains(pair.X));
        Assert.Contains(pairs, pair => lists[pair.X].Contains(pair.Y));
        Assert.Contains(pairs, pair => !lists[pair.X].Contains(pair.Y));
    }

    // For each key, two candidates equal on every earlier key differ on this one, and every later key favours
    // the candidate that should come second. The requester is at 0150 with TestData.Profile.
    [Theory]
    [Trait("Spec", "S60")]
    [Trait("Spec", "S60.1")]
    [Trait("Spec", "S60.2")]
    [Trait("Spec", "S60.3")]
    [Trait("Spec", "S60.4")]
    [Trait("Spec", "S60.5")]
    [InlineData("S60.1")]
    [InlineData("S60.2")]
    [InlineData("S60.3")]
    [InlineData("S60.4")]
    [InlineData("S60.5")]
    [InlineData("account-id")]
    public async Task Order_follows_S60_keys(string key)
    {
        var a = await ClimberAsync();
        var (first, second) = key switch
        {
            // 3 hours of overlap against 2; second is nearer.
            "S60.1" => (await ClimberAsync("0355"), await ClimberAsync("0250", Windows(18, 20))),
            // Grade fit 2 against 0; second is nearer.
            "S60.2" => (await ClimberAsync("0355"), await ClimberAsync("0250", profile => profile with
            {
                Disciplines = [new DisciplineEntryRequest("bouldering", ["indoor"], new GradeRangeRequest("8A", "9A"), new GradeRangeRequest("3", "4"))],
            })),
            // A shared active gym against none; second is nearer.
            "S60.3" => (await ClimberAsync("0355"), await ClimberAsync("0250", Gyms(FixtureGyms.Active[1].Id))),
            // Nearer; second was active more recently.
            "S60.4" => (await ClimberAsync("0250"), await LaterAsync(() => ClimberAsync("0355"))),
            // Active more recently; same place.
            "S60.5" => await Reversed(await ClimberAsync("0250"), await LaterAsync(() => ClimberAsync("0250"))),
            // Equal on every key.
            _ => (await ClimberAsync("0250"), await ClimberAsync("0250")),
        };

        var list = await ListAsync(a);

        if (key == "account-id")
        {
            Assert.Equal(2, list.Count);
            Assert.True(list[0].AccountId.CompareTo(list[1].AccountId) < 0);
        }
        else
        {
            Assert.Equal([first.Name, second.Name], Names(list));
        }

        static Task<(TestClimber, TestClimber)> Reversed(TestClimber older, TestClimber newer) => Task.FromResult((newer, older));
    }

    // The requester and one candidate share only a gym that is inactive (S21, M2: arranged below the API); it does
    // not count, so distance decides.
    [Fact]
    [Trait("Spec", "S60.3")]
    [Trait("SpecPartial", "S51")]
    [Trait("SpecPartial", "S21")]
    public async Task Inactive_shared_gym_does_not_count()
    {
        var a = await ClimberAsync();
        var sharesInactive = await ClimberAsync("0355", Gyms());
        var nearer = await ClimberAsync("0250", Gyms());
        await AddHomeGymAsync(a, FixtureGyms.Inactive.Id, 1);
        await AddHomeGymAsync(sharesInactive, FixtureGyms.Inactive.Id, 0);

        var list = await ListAsync(a);

        Assert.Equal([nearer.Name, sharesInactive.Name], Names(list));
        Assert.Equal([new CardGymResponse(FixtureGyms.Inactive.Name, Active: false)], list[1].HomeGyms);
    }

    [Fact]
    [Trait("SpecPartial", "S13.5")]
    [Trait("SpecPartial", "S54")]
    public async Task Grades_in_viewer_scale()
    {
        var lead = new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest("7-", "8"), new GradeRangeRequest("6", "8"));
        var french = new DisciplineEntryRequest("lead", ["indoor"], new GradeRangeRequest("6a", "7b"), new GradeRangeRequest("6a", "7b"));
        var bouldering = new DisciplineEntryRequest("bouldering", ["indoor"], new GradeRangeRequest("5", "6B"), new GradeRangeRequest("4", "7A"));
        var a = await ClimberAsync(change: profile => profile with { RopeScale = "french", Disciplines = [bouldering, french] });
        var b = await ClimberAsync(change: profile => profile with { Disciplines = [bouldering, lead] });

        var seenByA = Assert.Single(await ListAsync(a));
        var seenByB = Assert.Single(await ListAsync(b));

        Assert.Equal(
            [new GradeRangeResponse("5", "6B"), new GradeRangeResponse("6b+", "7b")],
            seenByA.Disciplines.Select(entry => entry.Grades));
        Assert.Equal(
            [new GradeRangeResponse("5", "6B"), new GradeRangeResponse("6", "8")],
            seenByB.Disciplines.Select(entry => entry.Grades));
    }

    // The profile chose 1300, 24.2 km from 3015; it is then moved to the retired 3089 below the API, the state a
    // profile is in when the table retires a postcode it keeps (S102.3). 3089 is 36.1 km from 3015.
    [Fact]
    [Trait("Spec", "S102")]
    [Trait("Spec", "S102.3")]
    [Trait("Spec", "S16")]
    public async Task Retired_postcode_distance_uses_retained_point()
    {
        Assert.True(FixturePostcodes.Get(FixturePostcodes.Retired).Retired);
        var a = await ClimberAsync("3015", profile => profile with { MaxTravelKm = 40 });
        var b = await ClimberAsync("1300", profile => profile with { MaxTravelKm = 40 });
        Assert.Equal("24 km", Assert.Single(await ListAsync(a)).Distance);

        await ExecuteAsync(
            "UPDATE profiles SET postcode = @postcode FROM accounts WHERE accounts.id = profiles.account_id AND accounts.email = @email",
            ("postcode", Factory.Data.Postcode(FixturePostcodes.Retired)),
            ("email", b.Account.Email));

        Assert.Equal("36 km", Assert.Single(await ListAsync(a)).Distance);
        Assert.Equal("36 km", Assert.Single(await ListAsync(b)).Distance);
    }

    // 0187 and 0661 are 1.94 km apart: the floor applies to the rounded distance, so they show "2 km".
    [Theory]
    [Trait("SpecPartial", "S52")]
    [InlineData("0150", "0250", "< 2 km")]
    [InlineData("0187", "0661", "2 km")]
    [InlineData("0150", "0560", "2 km")]
    [InlineData("0150", "1300", "13 km")]
    [InlineData("0560", "1300", "14 km")]
    public async Task Card_distance_is_whole_km_with_a_floor(string aPostcode, string bPostcode, string shown)
    {
        var a = await ClimberAsync(aPostcode);
        await ClimberAsync(bPostcode);

        Assert.Equal(shown, Assert.Single(await ListAsync(a)).Distance);
    }

    [Fact]
    [Trait("SpecPartial", "S51")]
    [Trait("SpecPartial", "S22")]
    [Trait("SpecPartial", "S54")]
    public async Task Card_shows_the_climber()
    {
        var a = await ClimberAsync(change: Windows(19, 22));
        var b = await ClimberAsync("0250", profile => profile with
        {
            Sex = "other",
            OwnGear = false,
            Certification = "brattkort-led",
            HomeGyms = [FixtureGyms.Active[1].Id, FixtureGyms.Active[0].Id],
            Windows = [new WindowRequest("tuesday", 18, 21), new WindowRequest("saturday", 10, 14)],
        });

        var card = Assert.Single(await ListAsync(a));

        Assert.Equal(b.Name, card.DisplayName);
        Assert.Equal("other", card.Sex);
        Assert.Equal(
            ["bouldering indoor 5-6B", "lead indoor+outdoor 6--7"],
            card.Disciplines.Select(entry => $"{entry.Discipline} {string.Join('+', entry.Venues)} {entry.Grades.Lowest}-{entry.Grades.Highest}"));
        Assert.Equal("brattkort-led", card.Certification);
        Assert.Equal(Texts.Get(CardBuilder.SelfDeclaredKey, Language.Bokmal), card.CertificationLabel);
        Assert.NotEqual(CardBuilder.SelfDeclaredKey, card.CertificationLabel);
        Assert.False(card.OwnGear);
        Assert.Equal(
            [new CardGymResponse(FixtureGyms.Active[1].Name, true), new CardGymResponse(FixtureGyms.Active[0].Name, true)],
            card.HomeGyms);
        Assert.Equal([new WindowResponse("tuesday", 19, 21)], card.Overlap);
        Assert.Equal("< 2 km", card.Distance);
    }

    [Fact]
    [Trait("SpecPartial", "S50")]
    [Trait("SpecPartial", "S53")]
    public async Task Card_never_shows_email_birth_date_postcode_or_accepted_sexes()
    {
        var a = await ClimberAsync();
        var b = await ClimberAsync("0250", profile => profile with { AcceptedSexes = ["female", "other"] });

        using var response = await GetAsync(a.Token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var card = Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        var names = Properties(card).Select(property => property.Name).ToList();
        var values = Properties(card).Select(property => property.Value)
            .Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).ToList();
        Assert.Empty(names.Intersect(["email", "dateOfBirth", "postcode", "acceptedSexes", "partner"], StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(b.Account.Email, values);
        Assert.DoesNotContain("0250", values);
        Assert.DoesNotContain(b.Account.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), values);

        static IEnumerable<JsonProperty> Properties(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Properties(property.Value).Prepend(property)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Properties),
            _ => [],
        };
    }

    // C46 checks the two files agree; this checks the codes are among their keys.
    [Fact]
    public void Every_suggestion_text_exists_in_both_languages() =>
        Assert.Empty(GetSuggestions.Codes.Append(CardBuilder.SelfDeclaredKey).SelectMany(code => new[] { Language.English, Language.Bokmal }
            .Where(language => Texts.Get(code, language) == code)
            .Select(language => $"{code} ({language})")));

    private sealed record TestClimber(AccountDraft Account, string Postcode, string Token, Func<ProfileRequest, ProfileRequest> Change)
    {
        public string Name => Account.DisplayName;
    }

    private static DisciplineEntryRequest Entry(string discipline, string venue) => discipline switch
    {
        "bouldering" => new(discipline, [venue], new GradeRangeRequest("5", "6A"), new GradeRangeRequest("5", "6A")),
        "ice" => new(discipline, [venue], new GradeRangeRequest("WI3", "WI4"), new GradeRangeRequest("WI3", "WI4")),
        _ => new(discipline, [venue], new GradeRangeRequest("6", "7"), new GradeRangeRequest("6", "7")),
    };

    private static Func<ProfileRequest, ProfileRequest> Windows(int start, int end) =>
        profile => profile with { Windows = [new WindowRequest("tuesday", start, end)] };

    private static Func<ProfileRequest, ProfileRequest> Gyms(params Guid[] gyms) => profile => profile with { HomeGyms = gyms };

    private static GeoPoint Point(string postcode) =>
        new(FixturePostcodes.Get(postcode).Latitude, FixturePostcodes.Get(postcode).Longitude);

    private static List<string> Names(IEnumerable<ClimberCardResponse> cards) => [.. cards.Select(card => card.DisplayName)];

    // The climber made last, a minute after everything before it, so its last activity is the most recent.
    private async Task<TestClimber> LaterAsync(Func<Task<TestClimber>> create)
    {
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        return await create();
    }

    // Registered, confirmed unless confirm is false, logged in, with TestData.Profile at postcode, changed by change.
    private async Task<TestClimber> ClimberAsync(
        string postcode = "0150", Func<ProfileRequest, ProfileRequest>? change = null, bool confirm = true)
    {
        var account = Factory.Data.Account();
        await RegisterAsync(account);
        if (confirm)
        {
            await ConfirmAsync(account);
        }

        var climber = new TestClimber(account, postcode, await LogInAsync(account), change ?? (profile => profile));
        await SaveProfileAsync(climber, climber.Change);
        return climber;
    }

    private async Task SaveProfileAsync(TestClimber climber, Func<ProfileRequest, ProfileRequest> change)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/climber/profile")
        {
            Content = JsonContent.Create(change(Factory.Data.Profile(climber.Account, climber.Postcode))),
        };
        using var response = await SendAsync(request, climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<List<ClimberCardResponse>> ListAsync(TestClimber climber)
    {
        using var response = await GetAsync(climber.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<SuggestionListResponse>(Ct))!.Candidates];
    }

    private async Task<HttpResponseMessage> GetAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        return await SendAsync(request, token);
    }

    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    private async Task RegisterAsync(AccountDraft account)
    {
        using var response = await Client().PostAsJsonAsync(
            "/api/climber/accounts",
            new RegistrationRequest(account.Email, account.Password, account.DateOfBirth, "nb", TermsVersion: 1, PrivacyVersion: 1),
            Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Through the link in the confirmation email (S49).
    private async Task ConfirmAsync(AccountDraft account)
    {
        var email = Factory.Email.Sent.Last(message => message.To == account.Email);
        var start = email.Body.IndexOf("#t=", StringComparison.Ordinal) + "#t=".Length;
        var end = email.Body.IndexOfAny([' ', '\r', '\n'], start);
        var token = end < 0 ? email.Body[start..] : email.Body[start..end];
        using var response = await Client().PostAsJsonAsync("/api/climber/email-confirmations", new EmailConfirmationRequest(token), Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<string> LogInAsync(AccountDraft account)
    {
        using var login = await Client().PostAsJsonAsync("/api/climber/login", new LoginRequest(account.Email, account.Password), Ct);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var token = SetCookieHeaderValue.ParseList([.. login.Headers.GetValues(HeaderNames.SetCookie)])
            .Single(cookie => cookie.Name == SessionCookie.Name).Value.Value!;
        Factory.Fixtures.Register("session-token", token);
        return token;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? token)
    {
        if (token is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{SessionCookie.Name}={token}");
        }

        return await Client().SendAsync(request, Ct);
    }

    // S21's deactivation is M2, so a profile that lists an inactive gym is arranged below the API.
    private Task AddHomeGymAsync(TestClimber climber, Guid gym, int position) => ExecuteAsync(
        "INSERT INTO home_gyms (account_id, gym_id, position) SELECT id, @gym, @position FROM accounts WHERE email = @email",
        ("gym", gym),
        ("position", position),
        ("email", climber.Account.Email));

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
