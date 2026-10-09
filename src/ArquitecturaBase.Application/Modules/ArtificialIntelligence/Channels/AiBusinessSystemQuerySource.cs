using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

internal sealed class AiBusinessSystemQuerySource(IAiBusinessInformationReader reader, IPermissionService permissions,
    TimeProvider timeProvider, IUserReader users) : ISystemQuerySource
{
    public IReadOnlyList<SystemQueryDefinition> Definitions { get; } =
    [new("business.information", 1, "Read confirmed business name, description, location, opening hours, services, policies and FAQ.",
        "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", [ArtificialIntelligencePermissions.BusinessRead])];

    public async Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
    {
        if (request.ToolId != "business.information" || request.RoleId is not null || request.RoleName is not null || request.HelpTopic is not null)
            return AiAssistantErrors.ToolRejected;
        if (await users.FindByIdAsync(request.ActorUserId, cancellationToken) is not { IsActive: true } ||
            !await permissions.HasPermissionAsync(request.ActorUserId, ArtificialIntelligencePermissions.BusinessRead, cancellationToken))
            return AiAssistantErrors.ToolRejected;
        var information = await reader.FindAsync(cancellationToken);
        if (information.Revision == 0) return new SystemQueryResponse("notConnected", null);
        string Label(string key) => AssistantTexts.Get("Business." + key, request.Culture);
        var rows = new List<SystemQueryRow>
        {
            new(null, [new(Label("Name"), information.Name), new(Label("Description"), information.Description),
                new(Label("Hours"), information.Hours), new(Label("Location"), information.Location),
                new(Label("Services"), information.Services), new(Label("Policies"), information.Policies), new(Label("Faq"), information.Faq)]),
        };
        if (information.Details is { } details)
        {
            rows.Add(new(null, [new(Label("Address"), details.Location.Address), new(Label("Reference"), details.Location.Reference), new(Label("Area"), details.Location.Area)]));
            rows.Add(new(null, [new(Label("TimeZone"), details.OpeningHours.TimeZoneId)]));
            foreach (var day in details.OpeningHours.Days)
                rows.Add(new(null, [new(Label("Day"), Label("Weekday." + day.Weekday)), new(Label("Hours"), FormatHours(day.Status, day.Periods, request.Culture))]));
            foreach (var date in details.OpeningHours.Exceptions)
                rows.Add(new(null, [new(Label("Date"), date.Date.ToString("d", CultureInfo.GetCultureInfo(request.Culture))), new(Label("Hours"), FormatHours(date.Status, date.Periods, request.Culture))]));
            foreach (var faq in details.Faq)
                rows.Add(new(faq.Id, [new(Label("Question"), faq.Question), new(Label("Answer"), faq.Answer)]));
        }
        var complete = rows.Count <= 50;
        return new SystemQueryResponse(complete ? "complete" : "truncated", new(Guid.CreateVersion7(), "business.information", 1,
            timeProvider.GetUtcNow().UtcDateTime, information.Revision, "businessInformation", [ArtificialIntelligencePermissions.BusinessRead], rows.Take(50).ToArray(), complete));
    }

    private static string FormatHours(string status, IReadOnlyList<BusinessOpeningPeriod> periods, string culture) => status == "open"
        ? string.Join(", ", periods.Select(p => p.Start + "–" + p.End + (p.NextDay ? " " + AssistantTexts.Get("Business.NextDay", culture) : "")))
        : AssistantTexts.Get("Business.Status." + status, culture);
}
