using System.Net;
using Bunit;
using ClimbOn.Client.Pages;
using ClimbOn.Client.Services;
using Xunit;

namespace ClimbOn.Client.Tests;

// The suggestion list shows the api's cards in the api's order (S60); the client orders and filters nothing (C7).
public sealed class SuggestionsPageTests : ClientTestContext
{
    private const string SuggestionsRoute = "/api/climber/suggestions";

    [Fact]
    public void Renders_cards_in_server_order()
    {
        Api.Answer(HttpMethod.Get, SuggestionsRoute, HttpStatusCode.OK, new
        {
            candidates = new[] { Card("Siri", "< 2 km"), Card("Anders", "12 km"), Card("Bjørn", "40 km") },
        });

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("article.card").Count));
        Assert.Equal(["Siri", "Anders", "Bjørn"], page.FindAll("article.card h2").Select(heading => heading.TextContent));
        Assert.Equal(["< 2 km", "12 km", "40 km"], page.FindAll("article.card .distance").Select(distance => distance.TextContent));
        Assert.Empty(MissingTexts);
    }

    // What S51 puts on a card, each value as the api sent it: own sex, own gear, each discipline with its venues and
    // grades, the S22 label, an inactive gym marked (S21), the overlap with the viewer.
    [Fact]
    public void Card_shows_what_the_api_sent()
    {
        Api.Answer(HttpMethod.Get, SuggestionsRoute, HttpStatusCode.OK, new
        {
            candidates = new[]
            {
                Card("Siri", "< 2 km"),
                Card("Anders", "12 km", sex: "male", ownGear: false, discipline: "bouldering", venue: "outdoor"),
            },
        });

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("article.card").Count));
        var card = page.FindAll("article.card")[0];
        Assert.Equal(Text("sex_female"), Value(card, Text("card_sex")));
        Assert.Equal(Text("value_yes"), Value(card, Text("card_own_gear")));
        var discipline = card.QuerySelector(".disciplines li")!.TextContent;
        Assert.Contains(Text("discipline_lead"), discipline);
        Assert.Contains(Text("venue_indoor"), discipline);
        Assert.Contains("(self-declared)", card.QuerySelector(".certification")!.TextContent);
        Assert.Contains(Text("certification_brattkort-led"), card.QuerySelector(".certification")!.TextContent);
        var gyms = card.QuerySelectorAll(".gyms li");
        Assert.Equal(2, gyms.Count);
        Assert.False(gyms[0].ClassList.Contains("inactive"));
        Assert.True(gyms[1].ClassList.Contains("inactive"));
        Assert.Contains(Text("card_gym_inactive"), gyms[1].TextContent);
        Assert.Equal(Text("day_monday") + " 18:00–24:00", card.QuerySelector(".overlap li")!.TextContent.Trim());
        Assert.Contains("6a–7a+", discipline);
        Assert.Equal(card.GetAttribute("aria-labelledby"), card.QuerySelector("h2")!.Id);

        var other = page.FindAll("article.card")[1];
        Assert.Equal(Text("sex_male"), Value(other, Text("card_sex")));
        Assert.Equal(Text("value_no"), Value(other, Text("card_own_gear")));
        var otherDiscipline = other.QuerySelector(".disciplines li")!.TextContent;
        Assert.Contains(Text("discipline_bouldering"), otherDiscipline);
        Assert.Contains(Text("venue_outdoor"), otherDiscipline);
        Assert.Empty(MissingTexts);
    }

    // S116: the api refuses an unconfirmed email address, and its message says to confirm it.
    [Fact]
    public void Unconfirmed_shows_confirm_message()
    {
        Api.Fail(HttpMethod.Get, SuggestionsRoute, HttpStatusCode.Forbidden, "email_not_confirmed", "Server text.");

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.Equal("email_not_confirmed", page.Find("[role=alert]").GetAttribute("data-code")));
        Assert.Equal("Server text.", page.Find("[role=alert]").TextContent);
        Assert.Empty(page.FindAll("article.card"));
        Assert.Empty(page.FindAll("[role=status]"));
    }

    [Fact]
    public void Empty_list_says_so()
    {
        Api.Answer(HttpMethod.Get, SuggestionsRoute, HttpStatusCode.OK, new { candidates = Array.Empty<object>() });

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.Equal(Text("suggestions_empty"), page.Find("[role=status]").TextContent));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void Shows_network_text_when_the_api_cannot_be_reached()
    {
        Api.Unreachable(HttpMethod.Get, SuggestionsRoute);

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.Equal(Text(ServerMessage.NetworkKey), page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("[role=status]"));
    }

    [Fact]
    public void Shows_every_text_in_bokmal()
    {
        UseLanguage(UiLanguage.Bokmal);
        Api.Answer(HttpMethod.Get, SuggestionsRoute, HttpStatusCode.OK, new { candidates = new[] { Card("Siri", "< 2 km") } });

        var page = Render<Suggestions>();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("article.card")));
        Assert.Contains("Mandag", page.Find(".overlap li").TextContent);
        Assert.Empty(MissingTexts);
    }

    // The value a card shows under one of its terms.
    private static string Value(AngleSharp.Dom.IElement card, string term) =>
        card.QuerySelectorAll("dt").Single(dt => dt.TextContent == term).NextElementSibling!.TextContent.Trim();

    private static object Card(
        string displayName,
        string distance,
        string sex = "female",
        bool ownGear = true,
        string discipline = "lead",
        string venue = "indoor") => new
    {
        accountId = Guid.NewGuid(),
        displayName,
        sex,
        disciplines = new[] { new { discipline, venues = new[] { venue }, grades = new { lowest = "6a", highest = "7a+" } } },
        certification = "brattkort-led",
        certificationLabel = "self-declared",
        ownGear,
        homeGyms = new[] { new { name = "Klatreverket", active = true }, new { name = "Old gym", active = false } },
        overlap = new[] { new { day = "monday", start = 18, end = 24 } },
        distance,
    };
}
