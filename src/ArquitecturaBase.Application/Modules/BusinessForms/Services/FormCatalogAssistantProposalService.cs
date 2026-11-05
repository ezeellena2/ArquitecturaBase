using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The catalog of a Home or channel proposal: a Manual one is created here, inactive and pending like H4's; a people catalog (H4)
// goes through its own acceptance. What the proposal already created is looked up before reading the proposal again.
internal sealed class FormCatalogAssistantProposalService(IAssistantProposalLookup lookup, IFormAuthoringReader reader,
    IFormCatalogRepository repository, IStructuredCatalogProposalSink people, FormCatalogAccessGuard accessGuard, IUnitOfWork unitOfWork)
    : IFormCatalogAssistantProposalService
{
    public async Task<Result<FormCatalogAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.RequestId is not { } requestId || requestId == Guid.Empty) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "requestId");
        if (request.ProposalId is not { } proposalId || proposalId == Guid.Empty) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "proposalId");
        if (await CreatedAsync(access.Value, proposalId, cancellationToken) is { } created) return created;
        var found = await lookup.FindAsync(access.Value, requestId, proposalId, cancellationToken);
        if (found.IsFailure) return FormCatalogErrors.ProposalUnavailable;
        if (!found.Value.IsCurrent) return FormCatalogErrors.ProposalReplaced;
        return await CreateCatalogAsync(access.Value, found.Value.Context, found.Value.Proposal, found.Value.Provenance, "app", cancellationToken);
    }

    // IA3-A: the channel's own conversation, where the person is both Actor and Subject, tapped «Crear catálogo» on the latest
    // proposal it showed; the channel checks that it is the latest one.
    public async Task<Result<FormCatalogAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
    {
        var context = request.Context;
        if (context.InteractionMode != AssistantInteractionMode.Assistant || context.SubjectUserId != context.ActorUserId) return FormDefinitionErrors.Forbidden;
        var access = await accessGuard.CheckActorAsync(context.ActorUserId, true, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (await CreatedAsync(access.Value, request.Proposal.Id, cancellationToken) is { } created) return created;
        return await CreateCatalogAsync(access.Value, context, request.Proposal, request.Provenance, null, cancellationToken);
    }

    public async Task<Result<FormCatalogAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken);
        if (access.IsFailure) return access.Error;
        return await CreatedAsync(access.Value, proposalId, cancellationToken) ?? new FormCatalogAssistantProposalResponse(proposalId, null);
    }

    private async Task<Result<FormCatalogAssistantProposalResponse>> CreateCatalogAsync(Guid actor, AssistantChannelContext context,
        AssistantObservedProposal proposal, AssistantOperationProvenance provenance, string? requestedIn, CancellationToken cancellationToken)
    {
        if (proposal is { Capability: { Id: BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, Version: 1 }, Payload: FormAuthoringCatalogProposal manual })
            return await CreateManualAsync(actor, proposal, manual, provenance, requestedIn, cancellationToken);
        if (proposal is not { Capability: { Id: BusinessFormCatalogAssistantCapabilitySource.ProposeId, Version: 1 }, Payload: FormCatalogProposalPayload filters })
            return FormCatalogErrors.ProposalUnavailable;
        if (await reader.ExistsCatalogNameAsync(context.Culture, filters.Name, cancellationToken)) return FieldErrors.On(FormCatalogErrors.NameTaken, "name");
        var accepted = await people.AcceptAsync(new StructuredCatalogProposalAcceptanceRequest(context, proposal, provenance), cancellationToken);
        return accepted.IsFailure ? accepted.Error : new FormCatalogAssistantProposalResponse(proposal.Id, accepted.Value.CatalogId);
    }

    private async Task<Result<FormCatalogAssistantProposalResponse>> CreateManualAsync(Guid actor, AssistantObservedProposal observed,
        FormAuthoringCatalogProposal proposal, AssistantOperationProvenance provenance, string? requestedIn, CancellationToken cancellationToken)
    {
        if (await reader.ExistsCatalogNameAsync(proposal.Culture, proposal.Name, cancellationToken)) return FieldErrors.On(FormCatalogErrors.NameTaken, "name");
        var evidence = JsonSerializer.Serialize(new FormCatalogProposalEvidence(proposal.Culture, actor, provenance.OccurredAtUtc,
            observed.TurnId, observed.Capability.Id, observed.Capability.Version, provenance, null, 0, proposal, requestedIn),
            JsonSerializerOptions.Web);
        return await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogAssistantProposalResponse>>(async ct =>
        {
            // The proposal's own lock, as in H4: two taps at once wait for each other and get the same catalog.
            await repository.LockAsync(observed.Id, ct);
            var current = await accessGuard.CheckActorAsync(actor, true, ct);
            if (current.IsFailure) return current.Error;
            if (await repository.GetByProposalAsync(observed.Id, ct) is { } prior) return new FormCatalogAssistantProposalResponse(observed.Id, prior.Id);
            var catalog = BusinessFormCatalog.CreateProposedManual(proposal.Culture, Texts(proposal.Culture, proposal.Name),
                Texts(proposal.Culture, proposal.Description), Enum.Parse<BusinessFormCatalogValueType>(proposal.ValueType, ignoreCase: true),
                observed.Id, evidence);
            var fields = catalog.SetFields(FormAuthoringProposalPolicy.CatalogFields(proposal), null);
            if (fields.IsFailure) return fields.Error;
            repository.Add(catalog);
            return new FormCatalogAssistantProposalResponse(observed.Id, catalog.Id);
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    // Only what this person's own proposal created counts.
    private async Task<FormCatalogAssistantProposalResponse?> CreatedAsync(Guid actor, Guid proposalId, CancellationToken ct) =>
        await reader.FindCatalogByProposalAsync(proposalId, ct) is { } row
        && FormCatalogProposalPresentation.Evidence(row.EvidenceJson).RequestedByUserId == actor
            ? new FormCatalogAssistantProposalResponse(proposalId, row.Id) : null;

    private static Dictionary<string, string> Texts(string culture, string text) =>
        text.Length == 0 ? new Dictionary<string, string>() : new Dictionary<string, string> { [culture] = text };
}
