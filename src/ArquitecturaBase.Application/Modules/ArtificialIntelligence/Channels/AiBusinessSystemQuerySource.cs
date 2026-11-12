using System.Globalization;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

internal sealed class AiBusinessSystemQuerySource(IAiBusinessInformationReader reader, IPermissionService permissions,
    TimeProvider timeProvider, IUserReader users) : IAssistantCapabilitySource
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [new("business.information", 1, AssistantCapabilityKind.Read, "Read confirmed business name, description, location, opening hours, services, policies and FAQ.",
        "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", SystemQueryCapabilityCodec.OutputSchemaJson, new([ArtificialIntelligencePermissions.BusinessRead], []), new(1024, 16384, 50, 5))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Capability is not { Id: "business.information", Version: 1 } ||
            SystemQueryCapabilityCodec.ParseArguments<Arguments>(request.ArgumentsJson).IsFailure)
            return AiAssistantErrors.ToolRejected;
        if (await users.FindByIdAsync(request.Context.ActorUserId, cancellationToken) is not { IsActive: true } ||
            !await permissions.HasPermissionAsync(request.Context.ActorUserId, ArtificialIntelligencePermissions.BusinessRead, cancellationToken))
            return AiAssistantErrors.ToolRejected;
        var information = await reader.FindAsync(cancellationToken);
        if (information.Revision == 0) return SystemQueryCapabilityCodec.Response("notConnected", null);
        string Label(string key) => AssistantTexts.Get("Business." + key, request.Context.Culture);
        // Only confirmed facts: the texts saved before the guided editor (hours, location and answers) were never reviewed, and a
        // question without an answer is not a fact (Casos del asistente, 2026-11-11).
        var rows = new List<SystemQueryRow>
        {
            new(null, [new(Label("Name"), information.Name), new(Label("Description"), information.Description),
                new(Label("Services"), information.Services), new(Label("Policies"), information.Policies)]),
        };
        if (information.Details is { } details)
        {
            rows.Add(new(null, [new(Label("Address"), details.Location.Address), new(Label("Reference"), details.Location.Reference), new(Label("Area"), details.Location.Area)]));
            rows.Add(new(null, [new(Label("TimeZone"), details.OpeningHours.TimeZoneId)]));
            foreach (var day in details.OpeningHours.Days)
                rows.Add(new(null, [new(Label("Day"), Label("Weekday." + day.Weekday)), new(Label("Hours"), AiBusinessKnowledgePolicy.Hours(day.Status, day.Periods, request.Context.Culture))]));
            foreach (var date in details.OpeningHours.Exceptions)
                rows.Add(new(null, [new(Label("Date"), date.Date.ToString(request.Context.Culture.StartsWith("en", StringComparison.Ordinal) ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture)), new(Label("Hours"), AiBusinessKnowledgePolicy.Hours(date.Status, date.Periods, request.Context.Culture))]));
            foreach (var faq in details.Faq.Where(AiBusinessKnowledgePolicy.Answered))
                rows.Add(new(faq.Id, [new(Label("Question"), faq.Question), new(Label("Answer"), faq.Answer)]));
        }
        var complete = rows.Count <= 50;
        return SystemQueryCapabilityCodec.Response(complete ? "complete" : "truncated", new(Guid.CreateVersion7(), "business.information", 1,
            timeProvider.GetUtcNow().UtcDateTime, information.Revision, "businessInformation", [ArtificialIntelligencePermissions.BusinessRead], rows.Take(50).ToArray(), complete));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryCapabilityCodec.ParseOutput(capability, outputJson, Definitions, "complete", "truncated", "notConnected");

    private sealed record Arguments;
}
