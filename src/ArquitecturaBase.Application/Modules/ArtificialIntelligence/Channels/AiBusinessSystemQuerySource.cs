using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
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
        // The same confirmed facts as the channels (B5): a question without an answer, a day without information and a text saved
        // before the guided editor are never facts, and without a confirmed day the zone is not one either. The first row brings
        // name, description, services and policies; then the location, each hour and each answered question with its id.
        var knowledge = AiBusinessKnowledgePolicy.Build(information, request.Context.Culture);
        BusinessKnowledgeFact[] Facts(params BusinessKnowledgeSectionKey[] keys) =>
            [.. knowledge.Sections.Where(section => keys.Contains(section.Key)).SelectMany(section => section.Facts)];
        static SystemQueryValue Value(BusinessKnowledgeFact fact) => new(fact.Label, fact.Text);
        var rows = new[] { Facts(BusinessKnowledgeSectionKey.Details, BusinessKnowledgeSectionKey.Services), Facts(BusinessKnowledgeSectionKey.Location) }
            .Where(facts => facts.Length > 0).Select(facts => new SystemQueryRow(null, [.. facts.Select(Value)])).ToList();
        rows.AddRange(Facts(BusinessKnowledgeSectionKey.Hours).Select(fact => new SystemQueryRow(null, [Value(fact)])));
        rows.AddRange(Facts(BusinessKnowledgeSectionKey.Answers).Select(fact =>
            new SystemQueryRow(Guid.ParseExact(fact.Id["faq.".Length..], "N"), [Value(fact)])));
        var complete = rows.Count <= 50;
        return SystemQueryCapabilityCodec.Response(complete ? "complete" : "truncated", new(Guid.CreateVersion7(), "business.information", 1,
            timeProvider.GetUtcNow().UtcDateTime, information.Revision, "businessInformation", [ArtificialIntelligencePermissions.BusinessRead], rows.Take(50).ToArray(), complete));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryCapabilityCodec.ParseOutput(capability, outputJson, Definitions, "complete", "truncated", "notConnected");

    private sealed record Arguments;
}
