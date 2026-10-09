namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
public sealed record UpdateAiBusinessInformationHttpRequest(string Name, string Description, string Hours, string Location,
    string Services, string Policies, string Faq, long ExpectedRevision, Guid RequestId, AiBusinessDetailsHttpRequest? Details = null)
{
    public override string ToString() => "UpdateAiBusinessInformationHttpRequest { content: [REDACTED] }";
}
public sealed record BusinessLocationHttpRequest(string Mode, string Address, string Reference, string Area, double? Latitude, double? Longitude, string? PlaceId = null)
{
    public override string ToString() => "BusinessLocationHttpRequest { content: [REDACTED] }";
}
public sealed record BusinessOpeningPeriodHttpRequest(string Start, string End, bool NextDay);
public sealed record BusinessOpeningDayHttpRequest(int Weekday, string Status, IReadOnlyList<BusinessOpeningPeriodHttpRequest> Periods);
public sealed record BusinessSpecialDateHttpRequest(DateOnly Date, string Status, IReadOnlyList<BusinessOpeningPeriodHttpRequest> Periods);
public sealed record BusinessOpeningHoursHttpRequest(string TimeZoneId, IReadOnlyList<BusinessOpeningDayHttpRequest> Days, IReadOnlyList<BusinessSpecialDateHttpRequest> Exceptions);
public sealed record BusinessFaqHttpRequest(Guid Id, string Question, string Answer)
{
    public override string ToString() => "BusinessFaqHttpRequest { content: [REDACTED] }";
}
public sealed record AiBusinessDetailsHttpRequest(BusinessLocationHttpRequest Location, BusinessOpeningHoursHttpRequest OpeningHours, IReadOnlyList<BusinessFaqHttpRequest> Faq)
{
    public override string ToString() => "AiBusinessDetailsHttpRequest { content: [REDACTED] }";
}
