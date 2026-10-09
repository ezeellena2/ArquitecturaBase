using System.Globalization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed record BusinessLocation(string Mode, string Address, string Reference, string Area, double? Latitude, double? Longitude, string? PlaceId = null)
{
    public string? Link => !string.IsNullOrWhiteSpace(PlaceId)
        ? "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(Address) ? "Place" : Address.Trim())
            + "&query_place_id=" + Uri.EscapeDataString(PlaceId.Trim())
        : Latitude is { } latitude && Longitude is { } longitude
        ? "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(
            latitude.ToString("0.######", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.######", CultureInfo.InvariantCulture))
        : null;
    public static bool IsValidPlaceId(string? id) => id is null || id.Trim() is { Length: > 0 and <= 1024 } text
        && text.All(character => character is >= '!' and <= '~');
    public override string ToString() => "BusinessLocation { content: [REDACTED] }";
}
public sealed record BusinessOpeningPeriod(string Start, string End, bool NextDay);
public sealed record BusinessOpeningDay(int Weekday, string Status, IReadOnlyList<BusinessOpeningPeriod> Periods);
public sealed record BusinessSpecialDate(DateOnly Date, string Status, IReadOnlyList<BusinessOpeningPeriod> Periods);
public sealed record BusinessOpeningHours(string TimeZoneId, IReadOnlyList<BusinessOpeningDay> Days, IReadOnlyList<BusinessSpecialDate> Exceptions);
public sealed record BusinessFaqEntry(Guid Id, string Question, string Answer)
{
    public override string ToString() => "BusinessFaqEntry { content: [REDACTED] }";
}
public sealed record AiBusinessDetails(BusinessLocation Location, BusinessOpeningHours OpeningHours, IReadOnlyList<BusinessFaqEntry> Faq)
{
    public bool HasConfirmedFacts() => !string.IsNullOrWhiteSpace(Location.Address) || !string.IsNullOrWhiteSpace(Location.Area)
        || Location.Latitude.HasValue || Location.PlaceId is not null || OpeningHours.Days.Any(x => x.Status != "unknown") || OpeningHours.Exceptions.Count > 0
        || Faq.Any(x => !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer));

    public Result<AiBusinessDetails> Normalize()
    {
        if (Location is null || !ValidLocation(Location)) return AiBusinessInformationErrors.InvalidLocation;
        if (OpeningHours is null || !ValidHours(OpeningHours)) return AiBusinessInformationErrors.InvalidHours;
        if (Faq is null || Faq.Count > 20 || Faq.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Question)
            || x.Question.Trim().Length > 200 || x.Answer is null || x.Answer.Trim().Length > 1000)
            || Faq.Select(x => x.Id).Distinct().Count() != Faq.Count) return AiBusinessInformationErrors.InvalidFaq;

        var location = Location with { Address = Location.Address.Trim(), Reference = Location.Reference.Trim(), Area = Location.Area.Trim(), PlaceId = Location.PlaceId?.Trim(),
            Latitude = Location.Latitude is { } latitude ? Math.Round(latitude, 6, MidpointRounding.AwayFromZero) : null,
            Longitude = Location.Longitude is { } longitude ? Math.Round(longitude, 6, MidpointRounding.AwayFromZero) : null };
        var hours = OpeningHours with {
            Days = Array.AsReadOnly(OpeningHours.Days.OrderBy(x => x.Weekday).Select(x => x with { Periods = CopyPeriods(x.Periods) }).ToArray()),
            Exceptions = Array.AsReadOnly(OpeningHours.Exceptions.OrderBy(x => x.Date).Select(x => x with { Periods = CopyPeriods(x.Periods) }).ToArray()) };
        return this with { Location = location, OpeningHours = hours,
            Faq = Array.AsReadOnly(Faq.Select(x => x with { Question = x.Question.Trim(), Answer = x.Answer.Trim() }).ToArray()) };
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<BusinessOpeningPeriod> CopyPeriods(IReadOnlyList<BusinessOpeningPeriod> periods) =>
        Array.AsReadOnly(periods.OrderBy(x => x.Start, StringComparer.Ordinal).ToArray());

    private static bool ValidLocation(BusinessLocation location)
    {
        if (location.Mode is not ("unknown" or "local" or "area" or "both")
            || new[] { location.Address, location.Reference, location.Area }.Any(x => x is null || x.Trim().Length > 500)
            || location.Latitude.HasValue != location.Longitude.HasValue || !BusinessLocation.IsValidPlaceId(location.PlaceId)
            || location.PlaceId is not null && location.Latitude.HasValue) return false;
        if (location.Latitude is { } latitude && (!double.IsFinite(latitude) || latitude is < -90 or > 90
            || !double.IsFinite(location.Longitude!.Value) || location.Longitude.Value is < -180 or > 180)) return false;
        if (location.Mode == "unknown") return location.PlaceId is null && !location.Latitude.HasValue && string.IsNullOrWhiteSpace(location.Address)
            && string.IsNullOrWhiteSpace(location.Reference) && string.IsNullOrWhiteSpace(location.Area);
        if (location.Mode == "area") return location.PlaceId is null && !location.Latitude.HasValue && string.IsNullOrWhiteSpace(location.Address) && string.IsNullOrWhiteSpace(location.Reference);
        return location.Mode != "local" || string.IsNullOrWhiteSpace(location.Area);
    }

    private static bool ValidHours(BusinessOpeningHours hours)
    {
        if (string.IsNullOrEmpty(hours.TimeZoneId) || hours.TimeZoneId.Length > 100
            || !TimeZoneInfo.TryFindSystemTimeZoneById(hours.TimeZoneId, out var zone) || (!zone.HasIanaId && hours.TimeZoneId != "UTC")
            || hours.Days is null || hours.Days.Count != 7 || hours.Days.Any(x => x is null || x.Weekday is < 0 or > 6 || !ValidDay(x.Status, x.Periods))
            || hours.Days.Select(x => x.Weekday).Distinct().Count() != 7 || hours.Exceptions is null || hours.Exceptions.Count > 32
            || hours.Exceptions.Any(x => x is null || x.Date.Year is < 1900 or > 9998 || !ValidDay(x.Status, x.Periods))
            || hours.Exceptions.Select(x => x.Date).Distinct().Count() != hours.Exceptions.Count) return false;
        var intervals = hours.Days.SelectMany(day => Intervals(day.Status, day.Periods, day.Weekday * 1440)).ToArray();
        if (Overlaps(intervals.Concat(intervals.Select(x => (x.Start + 10080, x.End + 10080))))) return false;
        foreach (var exception in hours.Exceptions)
        {
            var adjacent = Enumerable.Range(-1, 3).SelectMany(offset => {
                var date = exception.Date.AddDays(offset);
                var special = hours.Exceptions.SingleOrDefault(x => x.Date == date);
                var weekly = hours.Days.Single(x => x.Weekday == ((int)date.DayOfWeek + 6) % 7);
                return special is null ? Intervals(weekly.Status, weekly.Periods, (offset + 1) * 1440)
                    : Intervals(special.Status, special.Periods, (offset + 1) * 1440);
            });
            if (Overlaps(adjacent)) return false;
        }
        return true;
    }

    private static bool ValidDay(string status, IReadOnlyList<BusinessOpeningPeriod> periods)
    {
        if (periods is null || status is not ("unknown" or "open" or "closed" or "allDay")) return false;
        if (status != "open") return periods.Count == 0;
        return periods.Count is > 0 and <= 4 && periods.All(x => x is not null && ParseTime(x.Start, out var start)
            && ParseTime(x.End, out var end) && end + (x.NextDay ? 1440 : 0) - start is > 0 and <= 1440);
    }
    private static bool ParseTime(string value, out int minutes)
    {
        var valid = TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        minutes = time.Hour * 60 + time.Minute;
        return valid && time.Minute % 15 == 0;
    }
    private static IEnumerable<(int Start, int End)> Intervals(string status, IReadOnlyList<BusinessOpeningPeriod> periods, int offset)
    {
        if (status == "allDay") yield return (offset, offset + 1440);
        if (status != "open") yield break;
        foreach (var period in periods)
        {
            ParseTime(period.Start, out var start);
            ParseTime(period.End, out var end);
            yield return (offset + start, offset + end + (period.NextDay ? 1440 : 0));
        }
    }
    private static bool Overlaps(IEnumerable<(int Start, int End)> intervals)
    {
        var ordered = intervals.OrderBy(x => x.Start).ToArray();
        return ordered.Where((x, index) => index > 0 && x.Start < ordered[index - 1].End).Any();
    }
    public override string ToString() => "AiBusinessDetails { content: [REDACTED] }";
}
