using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The draft of a Home or channel proposal goes through the editor's own save, so it obeys the same rules. What the proposal
// already created is looked up before reading the proposal again: a second tap returns the same draft, even once it expired.
internal sealed class FormDraftProposalService(IAssistantProposalLookup lookup, IFormAuthoringReader reader, IFormDraftService drafts,
    FormDraftAccessGuard accessGuard) : IFormDraftProposalService
{
    public async Task<Result<FormDraftAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "requestId");
        if (request.ProposalId is not { } proposalId || proposalId == Guid.Empty) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "proposalId");
        if (await CreatedAsync(access.Value, proposalId, cancellationToken) is { } created) return created;
        var found = await lookup.FindAsync(access.Value, requestId, proposalId, [BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId], cancellationToken);
        if (found.IsFailure || found.Value.Proposal is not { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Version: 1 },
                Payload: FormAuthoringFormProposal proposal })
            return FormDefinitionErrors.ProposalUnavailable;
        if (!found.Value.IsCurrent) return FormDefinitionErrors.ProposalReplaced;
        return await CreateDraftAsync(access.Value, found.Value.Proposal, proposal, found.Value.Provenance, "app", cancellationToken);
    }

    // IA3-A: the channel's own conversation, where the person is both Actor and Subject, tapped «Crear borrador» on the latest
    // proposal it showed; the channel checks that it is the latest one.
    public async Task<Result<FormDraftAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
    {
        var context = request.Context;
        if (context.InteractionMode != AssistantInteractionMode.Assistant || context.SubjectUserId != context.ActorUserId) return FormDefinitionErrors.Forbidden;
        var access = await accessGuard.CheckActorAsync(context.ActorUserId, true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.Proposal is not { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Version: 1 }, Payload: FormAuthoringFormProposal proposal })
            return FormDefinitionErrors.ProposalUnavailable;
        if (await CreatedAsync(access.Value, request.Proposal.Id, cancellationToken) is { } created) return created;
        return await CreateDraftAsync(access.Value, request.Proposal, proposal, request.Provenance, null, cancellationToken);
    }

    public async Task<Result<FormDraftAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        return await CreatedAsync(access.Value, proposalId, cancellationToken) ?? new FormDraftAssistantProposalResponse(proposalId, null);
    }

    private async Task<Result<FormDraftAssistantProposalResponse>> CreateDraftAsync(Guid actor, AssistantObservedProposal observed,
        FormAuthoringFormProposal proposal, AssistantOperationProvenance provenance, string? requestedIn, CancellationToken ct)
    {
        var catalogs = await CheckCatalogsAsync(actor, proposal, ct);
        if (catalogs.IsFailure) return catalogs.Error;
        if (await reader.ExistsDefinitionNameAsync(proposal.Culture, proposal.Name, ct)) return FieldErrors.On(FormDefinitionErrors.NameTaken, "name");
        var evidence = new FormDraftProposalEvidence(proposal.Culture, actor, provenance.OccurredAtUtc, observed.TurnId,
            observed.Capability.Id, observed.Capability.Version, provenance, proposal, requestedIn);
        // The proposal is also the request of its draft change: two taps at once wait for each other and get the same draft.
        var draft = FormAuthoringProposalPolicy.Draft(observed.Id, proposal) with
        {
            Proposal = new FormDraftProposalOrigin(observed.Id, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web)),
            ActorUserId = actor,
        };
        var saved = await drafts.CreateAsync(draft, ct);
        return saved.IsFailure ? saved.Error : new FormDraftAssistantProposalResponse(observed.Id, saved.Value.Definition.Id);
    }

    // Only what this person's own proposal created counts.
    private async Task<FormDraftAssistantProposalResponse?> CreatedAsync(Guid actor, Guid proposalId, CancellationToken ct) =>
        await reader.FindDefinitionByProposalAsync(proposalId, ct) is { } row
        && JsonSerializer.Deserialize<FormDraftProposalEvidence>(row.EvidenceJson, JsonSerializerOptions.Web)?.RequestedByUserId == actor
            ? new FormDraftAssistantProposalResponse(proposalId, row.Id) : null;

    // As when proposing: a catalog question needs an active catalog, read with the person's own permission.
    private async Task<Result> CheckCatalogsAsync(Guid actor, FormAuthoringFormProposal proposal, CancellationToken ct)
    {
        var ids = proposal.Questions.Where(question => question.Catalog is not null).Select(question => question.Catalog!.Id).Distinct().ToArray();
        if (ids.Length == 0) return Result.Success();
        var readable = await accessGuard.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, ct);
        var active = readable ? (await reader.ListCatalogsAsync(ids, ct)).Where(catalog => catalog.IsActive).Select(catalog => catalog.Id).ToHashSet() : [];
        for (var index = 0; index < proposal.Questions.Count; index++)
            if (proposal.Questions[index].Catalog is { } catalog && !active.Contains(catalog.Id))
                return FieldErrors.On(FormDefinitionErrors.CatalogUnavailable, $"questions[{index}].catalogId");
        return Result.Success();
    }
}
