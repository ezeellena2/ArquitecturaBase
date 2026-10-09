using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiBusinessDetailsTests
{
    [Fact]
    public void A_place_identifier_is_normalized_and_opens_the_exact_Google_Maps_place()
    {
        var value = Details() with { Location = new("local", " My own address ", "", "", null, null, " ChIJ_fictionalPlace ") };
        var normalized = value.Normalize().Value;
        Assert.Equal("ChIJ_fictionalPlace", normalized.Location.PlaceId);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=My%20own%20address&query_place_id=ChIJ_fictionalPlace", normalized.Location.Link);
        Assert.True(normalized.HasConfirmedFacts());
        Assert.DoesNotContain("ChIJ_fictionalPlace", normalized.Location.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("area", null, "ChIJ_example")]
    [InlineData("unknown", null, "ChIJ_example")]
    [InlineData("local", 12.0, "ChIJ_example")]
    [InlineData("local", null, "invalid\nidentifier")]
    public void A_place_cannot_be_mixed_with_a_point_or_an_incompatible_location(string mode, double? latitude, string id)
    {
        var value = Details() with { Location = new(mode, "", "", "", latitude, latitude, id) };
        Assert.True(value.Normalize().IsFailure);
    }

    private static AiBusinessDetails Details() => new(new("unknown", "", "", "", null, null),
        new("America/Argentina/Buenos_Aires", Enumerable.Range(0, 7).Select(i => new BusinessOpeningDay(i, "unknown", [])).ToArray(), []), []);

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        var value = Details() with { Location = new("local", "", "", "", latitude, longitude) };
        Assert.True(value.Normalize().IsFailure);
    }
    [Fact]
    public void Normalization_copies_collections_and_generates_a_safe_link()
    {
        var faqs = new List<BusinessFaqEntry> { new(Guid.NewGuid(), " Question ", " Answer ") };
        var value = Details() with { Location = new("local", " Address ", "", "", -34.6037, -58.3816), Faq = faqs };
        var normalized = value.Normalize().Value;
        faqs.Clear();
        Assert.Single(normalized.Faq);
        Assert.Equal("Question", normalized.Faq[0].Question);
        Assert.Equal("Address", normalized.Location.Address);
        Assert.Equal("https://www.google.com/maps/search/?api=1&query=-34.6037%2C-58.3816", normalized.Location.Link);
        Assert.DoesNotContain("Address", normalized.ToString(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("18:00", "09:00", false)]
    [InlineData("09:00", "18:00", true)]
    [InlineData("9:00", "18:00", false)]
    [InlineData("09:00", "09:00", false)]
    public void Invalid_periods_are_rejected(string start, string end, bool nextDay)
    {
        var value = Details();
        value = value with { OpeningHours = value.OpeningHours with { Days = value.OpeningHours.Days.Select(x => x.Weekday == 0
            ? x with { Status = "open", Periods = [new(start, end, nextDay)] } : x).ToArray() } };
        Assert.True(value.Normalize().IsFailure);
    }
    [Fact]
    public void Sunday_overnight_hours_cannot_overlap_Monday()
    {
        var value = Details();
        var days = value.OpeningHours.Days.ToArray();
        days[6] = new(6, "open", [new("22:00", "02:00", true)]);
        days[0] = new(0, "open", [new("01:00", "09:00", false)]);
        Assert.True((value with { OpeningHours = value.OpeningHours with { Days = days } }).Normalize().IsFailure);
        days[0] = new(0, "open", [new("02:00", "09:00", false)]);
        Assert.True((value with { OpeningHours = value.OpeningHours with { Days = days } }).Normalize().IsSuccess);
    }
    [Fact]
    public void A_special_date_cannot_overlap_the_previous_weekly_overnight_period()
    {
        var value = Details();
        var days = value.OpeningHours.Days.ToArray();
        days[0] = new(0, "open", [new("22:00", "02:00", true)]);
        var hours = value.OpeningHours with { Days = days, Exceptions = [new(new DateOnly(2026, 10, 6), "allDay", [])] };
        Assert.True((value with { OpeningHours = hours }).Normalize().IsFailure);
    }
    [Fact]
    public void Duplicate_dates_days_or_FAQ_identifiers_are_rejected()
    {
        var value = Details();
        Assert.True((value with { OpeningHours = value.OpeningHours with { Days = [.. value.OpeningHours.Days, value.OpeningHours.Days[0]] } }).Normalize().IsFailure);
        var date = new BusinessSpecialDate(new(2026, 10, 5), "closed", []);
        Assert.True((value with { OpeningHours = value.OpeningHours with { Exceptions = [date, date] } }).Normalize().IsFailure);
        var faq = new BusinessFaqEntry(Guid.NewGuid(), "Question", "");
        Assert.True((value with { Faq = [faq, faq] }).Normalize().IsFailure);
    }
    [Fact]
    public void Unknown_hours_and_unanswered_questions_do_not_invent_confirmed_facts()
    {
        var value = Details() with { Faq = [new(Guid.NewGuid(), "Question", "")] };
        Assert.False(value.Normalize().Value.HasConfirmedFacts());
        Assert.True((value with { Faq = [new(Guid.NewGuid(), "Question", "Answer")] }).Normalize().Value.HasConfirmedFacts());
    }
    [Theory]
    [InlineData("Unknown/Zone")]
    [InlineData("Eastern Standard Time")]
    public void Hours_require_a_known_IANA_time_zone(string zone)
    {
        var value = Details();
        Assert.True((value with { OpeningHours = value.OpeningHours with { TimeZoneId = zone } }).Normalize().IsFailure);
    }
}
