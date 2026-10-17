using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormCatalogAssistantCapabilitySource(IFormCatalogFilterReader filters, IUserReader users,
    IPermissionService permissions) : IAssistantCapabilitySource
{
    public const string MetadataId = "forms.catalog-filters.metadata";
    public const string ProposeId = "forms.catalog-filters.propose";
    public const int Version = 1;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new(MetadataId, 1, AssistantCapabilityKind.Read, "Read the fixed users filter grammar and exact role IDs. No people, sample identities or SQL are available.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", FormCatalogAssistantCodec.MetadataSchema,
            new([BusinessFormsPermissions.CatalogsRead], []), new(1024, 16384, 200, 5)),
        new(ProposeId, 1, AssistantCapabilityKind.Proposal, "Propose an inactive users Filters catalog for human review, using the latest complete metadata read from this turn. Keep name and description in the conversation language. Use observed role IDs only. Never remove an unknown essential role. Report unsupported columns visibly as limitations; never invent SQL, tables or sample people.",
            FormCatalogAssistantCodec.ArgumentsSchema, FormCatalogAssistantCodec.ProposalSchema,
            new([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], []), new(8192, 16384, 1, 5)),
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        if (request.Capability.Version != Version || request.Capability.Id is not (MetadataId or ProposeId) || request.Context.Culture is not ("es" or "en"))
            return FormCatalogErrors.ContentInvalid;
        if (await users.FindByIdAsync(request.Context.ActorUserId, ct) is not { IsActive: true } ||
            !await permissions.HasPermissionAsync(request.Context.ActorUserId, BusinessFormsPermissions.CatalogsRead, ct) ||
            request.Capability.Id == ProposeId && !await permissions.HasPermissionAsync(request.Context.ActorUserId, BusinessFormsPermissions.CatalogsManage, ct))
            return FormDefinitionErrors.Forbidden;
        if (request.Capability.Id == MetadataId)
        {
            if (!FormCatalogAssistantCodec.EmptyArguments(request.ArgumentsJson)) return FormCatalogErrors.ContentInvalid;
            var roles = await filters.ListRolesAsync(ct);
            if (roles.Count > 200) return FormCatalogErrors.ContentInvalid;
            var payload = new FormCatalogMetadataPayload("users", [
                new("name", "text", true, true, true, ["contains", "equals"], null),
                new("role", "text", true, true, true, ["has"], roles),
                new("active", "boolean", true, true, true, ["equals"], [new("true", FormCatalogTexts.ActiveValue("true", request.Context.Culture)), new("false", FormCatalogTexts.ActiveValue("false", request.Context.Culture))])]);
            return new AssistantCapabilityResponse("complete", payload, [], roles.Count);
        }
        var parsed = FormCatalogAssistantCodec.ParseArguments(request.ArgumentsJson);
        if (parsed.IsFailure) return parsed.Error;
        var reads = request.ObservedReads.Where(read => read.TurnId == request.TurnId && read.Capability.Id == MetadataId)
            .OrderByDescending(read => read.Ordinal).ToArray();
        if (reads.Length == 0 || reads.Select(read => read.Ordinal).Distinct().Count() != reads.Length ||
            reads[0] is not { Ordinal: >= 1, Capability.Version: Version, Payload: FormCatalogMetadataPayload metadata } ||
            !FormCatalogAssistantCodec.ValidMetadata(metadata)) return FormCatalogErrors.ContentInvalid;
        var proposal = new FormCatalogProposalPayload(reads[0].Ordinal, parsed.Value.Name, parsed.Value.Description,
            parsed.Value.Filters, parsed.Value.Limitations);
        var valid = FormCatalogProposalPolicy.Validate(proposal, metadata, request.Context.Culture);
        return valid.IsFailure ? valid.Error : new AssistantCapabilityResponse("proposed", proposal, [], 1);
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson)
    {
        if (capability.Version != Version) return FormCatalogErrors.ContentInvalid;
        if (capability.Id == MetadataId)
        {
            var result = FormCatalogAssistantCodec.ParseMetadata(outputJson);
            return result.IsFailure ? result.Error : result.Value;
        }
        if (capability.Id == ProposeId)
        {
            var result = FormCatalogAssistantCodec.ParseProposal(outputJson);
            return result.IsFailure ? result.Error : result.Value;
        }
        return FormCatalogErrors.ContentInvalid;
    }
}
