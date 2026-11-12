using System.Globalization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

/// <summary>
/// The confirmed facts of the business information, zone by zone (Casos del asistente). A question without an answer and a text
/// saved before the guided editor (hours, location and answers) are never facts: nobody reviewed the old texts, and they are never
/// parsed. The labels and texts are in the culture asked for.
/// </summary>
internal static class AiBusinessKnowledgePolicy
{
    public static BusinessKnowledge Build(AiBusinessInformationResponse information, string culture)
    {
        string Label(string key) => AssistantTexts.Get("Business." + key, culture);
        var details = information.Details;
        var sections = new List<BusinessKnowledgeSection>();
        if (information.Revision == 0)
        {
            sections.AddRange(Enum.GetValues<BusinessKnowledgeSectionKey>().Select(key => new BusinessKnowledgeSection(key, BusinessKnowledgeSectionState.Empty, [])));
            return new(BusinessKnowledgeAvailability.Available, 0, sections, 0);
        }

        sections.Add(Section(BusinessKnowledgeSectionKey.Details, "", Fact("details.name", Label("Name"), information.Name),
            Fact("details.description", Label("Description"), information.Description)));
        sections.Add(Section(BusinessKnowledgeSectionKey.Location, information.Location,
            Fact("location.address", Label("Address"), details?.Location.Address), Fact("location.reference", Label("Reference"), details?.Location.Reference),
            Fact("location.area", Label("Area"), details?.Location.Area)));
        var hours = details?.OpeningHours;
        var hourFacts = hours is null || (hours.Days.All(day => day.Status == "unknown") && hours.Exceptions.Count == 0) ? []
            : new[] { Fact("hours.timeZone", Label("TimeZone"), hours.TimeZoneId) }
                .Concat(hours.Days.Where(day => day.Status != "unknown").Select(day => Fact("hours.day." + day.Weekday.ToString(CultureInfo.InvariantCulture),
                    Label("Weekday." + day.Weekday.ToString(CultureInfo.InvariantCulture)), Hours(day.Status, day.Periods, culture))))
                .Concat(hours.Exceptions.Select(date => Fact("hours.date." + date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    date.Date.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture), Hours(date.Status, date.Periods, culture))))
                .ToArray();
        sections.Add(Section(BusinessKnowledgeSectionKey.Hours, information.Hours, hourFacts));
        sections.Add(Section(BusinessKnowledgeSectionKey.Services, "", Fact("services", Label("Services"), information.Services),
            Fact("policies", Label("Policies"), information.Policies)));
        var faq = details?.Faq ?? [];
        sections.Add(Section(BusinessKnowledgeSectionKey.Answers, information.Faq, [.. faq.Where(Answered)
            .Select(entry => Fact("faq." + entry.Id.ToString("N"), entry.Question.Trim(), entry.Answer))]));
        return new(BusinessKnowledgeAvailability.Available, information.Revision, sections, faq.Count(entry => !Answered(entry)));
    }

    public static bool Answered(BusinessFaqEntry entry) => !string.IsNullOrWhiteSpace(entry.Question) && !string.IsNullOrWhiteSpace(entry.Answer);

    public static string Hours(string status, IReadOnlyList<BusinessOpeningPeriod> periods, string culture) => status == "open"
        ? string.Join(", ", periods.Select(period => period.Start + "–" + period.End + (period.NextDay ? " " + AssistantTexts.Get("Business.NextDay", culture) : "")))
        : AssistantTexts.Get("Business.Status." + status, culture);

    private static BusinessKnowledgeSection Section(BusinessKnowledgeSectionKey key, string legacyText, params BusinessKnowledgeFact?[] facts)
    {
        var confirmed = facts.OfType<BusinessKnowledgeFact>().ToArray();
        var state = confirmed.Length > 0 ? BusinessKnowledgeSectionState.Loaded
            : string.IsNullOrWhiteSpace(legacyText) ? BusinessKnowledgeSectionState.Empty : BusinessKnowledgeSectionState.LegacyUnreviewed;
        return new(key, state, confirmed);
    }

    private static BusinessKnowledgeFact? Fact(string id, string label, string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : new(id, label, text.Trim());
}
