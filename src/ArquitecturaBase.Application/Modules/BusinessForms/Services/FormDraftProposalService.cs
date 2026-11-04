using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The draft of a Home proposal goes through the editor's own save, so it obeys the same rules. What the proposal already
// created is looked up before reading the proposal again: a second tap returns the same draft, even once it expired.
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
        var found = await lookup.FindAsync(access.Value, requestId, proposalId, cancellationToken);
        if (found.IsFailure || found.Value.Proposal is not { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, Version: 1 },
                Payload: FormAuthoringFormProposal proposal })
            return FormDefinitionErrors.ProposalUnavailable;
        if (!found.Value.IsCurrent) return FormDefinitionErrors.ProposalReplaced;
        var catalogs = await CheckCatalogsAsync(access.Value, proposal, cancellationToken);
        if (catalogs.IsFailure) return catalogs.Error;
        if (await reader.ExistsDefinitionNameAsync(proposal.Culture, proposal.Name, cancellationToken)) return FieldErrors.On(FormDefinitionErrors.NameTaken, "name");
        var evidence = new FormDraftProposalEvidence(proposal.Culture, access.Value, found.Value.Provenance.OccurredAtUtc, found.Value.Proposal.TurnId,
            found.Value.Proposal.Capability.Id, found.Value.Proposal.Capability.Version, found.Value.Provenance, proposal, "app");
        // The proposal is also the request of its draft change: two taps at once wait for each other and get the same draft.
        var draft = FormAuthoringProposalPolicy.Draft(proposalId, proposal) with
        {
            Proposal = new FormDraftProposalOrigin(proposalId, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web)),
        };
        var saved = await drafts.CreateAsync(draft, cancellationToken);
        return saved.IsFailure ? saved.Error : new FormDraftAssistantProposalResponse(proposalId, saved.Value.Definition.Id);
    }

    public async Task<Result<FormDraftAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        return await CreatedAsync(access.Value, proposalId, cancellationToken) ?? new FormDraftAssistantProposalResponse(proposalId, null);
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
