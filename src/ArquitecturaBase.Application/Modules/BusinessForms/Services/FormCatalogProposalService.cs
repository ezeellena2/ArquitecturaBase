using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogProposalService(IFormCatalogRepository repository, IFormCatalogFilterReader filters,
    IUserReader users, IPermissionService permissions, ICurrentUser currentUser, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, IFormCatalogReader catalogReader) : IFormCatalogProposalService, IStructuredCatalogProposalSink
{
    public async Task<Result<StructuredCatalogProposalAcceptedResponse>> AcceptAsync(StructuredCatalogProposalAcceptanceRequest request, CancellationToken cancellationToken)
    {
        var proposal = ValidateProposal(request);
        if (proposal.IsFailure) return proposal.Error;
        var access = await CheckActorAsync(request.Context.ActorUserId, cancellationToken);
        if (access.IsFailure) return access.Error;
        return await unitOfWork.ExecuteInTransactionAsync<Result<StructuredCatalogProposalAcceptedResponse>>(async ct =>
        {
            await repository.LockAsync(request.Proposal.Id, ct);
            var current = await CheckActorAsync(request.Context.ActorUserId, ct);
            if (current.IsFailure) return current.Error;
            if (await repository.GetByProposalAsync(request.Proposal.Id, ct) is { } prior)
            {
                var stored = FormCatalogProposalPresentation.Evidence(prior.ProposalEvidenceJson!);
                if (stored.RequestedByUserId != request.Context.ActorUserId || stored.TurnId != request.Proposal.TurnId
                    || stored.OriginalCulture != request.Context.Culture || stored.Provenance != request.Provenance
                    || JsonSerializer.Serialize(stored.OriginalProposal, JsonSerializerOptions.Web) != JsonSerializer.Serialize(proposal.Value, JsonSerializerOptions.Web))
                    return FormCatalogErrors.RevisionConflict;
                return Receipt(prior, stored);
            }
            var preview = await PreviewAsync(request.Context.ActorUserId, proposal.Value.Filters, request.Context.Culture, ct);
            if (preview.IsFailure) return preview.Error;
            var evidence = new FormCatalogProposalEvidence(request.Context.Culture, request.Context.ActorUserId,
                request.Provenance.OccurredAtUtc, request.Proposal.TurnId, request.Proposal.Capability.Id,
                request.Proposal.Capability.Version, request.Provenance, proposal.Value, preview.Value.TotalCount);
            var catalog = BusinessFormCatalog.CreateProposedFilters(request.Context.Culture, new Dictionary<string,string> { [request.Context.Culture] = proposal.Value.Name },
                new Dictionary<string,string> { [request.Context.Culture] = proposal.Value.Description }, JsonSerializer.Serialize(proposal.Value.Filters, JsonSerializerOptions.Web),
                request.Proposal.Id, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web));
            repository.Add(catalog);
            return Receipt(catalog, evidence);
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    public async Task<Result<FormCatalogSaveResponse>> ApproveAsync(Guid id, ApproveFormCatalogProposalRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        var access = await CheckActorAsync(actor, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "expectedRevision");
        if (request.SampleUserId is null || request.SampleUserId == Guid.Empty) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "sampleUserId");
        return await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogSaveResponse>>(async ct =>
        {
            await repository.LockAsync(id, ct);
            var current = await CheckActorAsync(actor, ct);
            if (current.IsFailure) return current.Error;
            var catalog = await repository.GetAsync(id, ct);
            if (catalog is null) return FormCatalogErrors.NotFound;
            if (catalog.ProposalState != BusinessFormCatalogProposalState.Pending || catalog.Revision != request.ExpectedRevision)
                return FormCatalogErrors.RevisionConflict;
            var evidence = FormCatalogProposalPresentation.Evidence(catalog.ProposalEvidenceJson!);
            var content = FormCatalogProposalPolicy.ValidateEdits(request.Name, request.Description, request.Filters, evidence.OriginalCulture);
            if (content.IsFailure) return content.Error;
            var preview = await PreviewAsync(request.SampleUserId.Value, request.Filters!, evidence.OriginalCulture, ct);
            if (preview.IsFailure) return preview.Error;
            var approved = catalog.ApproveProposal(request.ExpectedRevision.Value, actor, timeProvider.GetUtcNow().UtcDateTime,
                FormCatalogProposalPolicy.Texts(request.Name!), FormCatalogProposalPolicy.Texts(request.Description!), JsonSerializer.Serialize(request.Filters, JsonSerializerOptions.Web));
            if (approved.IsFailure) return approved.Error;
            return new FormCatalogSaveResponse(await MapAsync(catalog, actor, ct), new Dictionary<string,Guid>());
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    public async Task<Result<FormCatalogDetailResponse>> DiscardAsync(Guid id, DiscardFormCatalogProposalRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        var access = await CheckActorAsync(actor, cancellationToken);
        if (access.IsFailure) return access.Error;
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991) return FieldErrors.On(FormCatalogErrors.ContentInvalid, "expectedRevision");
        return await unitOfWork.ExecuteInTransactionAsync<Result<FormCatalogDetailResponse>>(async ct =>
        {
            await repository.LockAsync(id, ct);
            var current = await CheckActorAsync(actor, ct);
            if (current.IsFailure) return current.Error;
            var catalog = await repository.GetAsync(id, ct);
            if (catalog is null) return FormCatalogErrors.NotFound;
            var discarded = catalog.DiscardProposal(request.ExpectedRevision.Value, actor, timeProvider.GetUtcNow().UtcDateTime);
            if (discarded.IsFailure) return discarded.Error;
            return await MapAsync(catalog, actor, ct);
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    private async Task<Result> CheckActorAsync(Guid actor, CancellationToken ct) =>
        await users.FindByIdAsync(actor, ct) is { IsActive: true }
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsRead, ct)
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.CatalogsManage, ct)
            ? Result.Success() : FormDefinitionErrors.Forbidden;

    private async Task<Result<FormCatalogFilterPreviewResponse>> PreviewAsync(Guid sample, FormCatalogFilterDefinition definition, string culture, CancellationToken ct)
    {
        if (!await filters.ExistsActiveSampleUserAsync(sample, ct)) return FormCatalogErrors.NotFound;
        var roles = FormCatalogProposalPolicy.ValidateRoles(definition, await filters.ListRolesAsync(ct));
        if (roles.IsFailure) return roles.Error;
        var preview = await filters.FindPreviewAsync(sample, definition, ct);
        var localized = preview with { Items = preview.Items.Select(item => item with
        {
            Detail = definition.DetailColumn == "active" && item.Detail is not null ? FormCatalogTexts.ActiveValue(item.Detail, culture) : item.Detail,
        }).ToArray() };
        return localized.ExceedsLimit || localized.TotalCount > 200 || localized.Items.Any(item => !item.CanSend)
            ? FieldErrors.On(FormCatalogErrors.ContentInvalid, "filters") : localized;
    }

    private async Task<FormCatalogDetailResponse> MapAsync(BusinessFormCatalog catalog, Guid actor, CancellationToken ct)
    {
        int? usage = await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)
            ? await catalogReader.CountUsageAsync(catalog.Id, ct) : null;
        return await FormCatalogProposalPresentation.MapAsync(catalog, usage, users, ct);
    }

    private static Result<FormCatalogProposalPayload> ValidateProposal(StructuredCatalogProposalAcceptanceRequest request)
    {
        if (request.Context.InteractionMode != AssistantInteractionMode.Assistant || string.IsNullOrWhiteSpace(request.Context.ChannelId)
            || request.Context.SubjectUserId != request.Context.ActorUserId
            || request.Context.Culture is not ("es" or "en") || request.Provenance.ChannelId != request.Context.ChannelId
            || request.Provenance.OccurredAtUtc.Kind != DateTimeKind.Utc || request.Provenance.ModelProfileVersion < 1
            || request.Provenance.InstructionsVersion < 1 || string.IsNullOrWhiteSpace(request.Provenance.ModelProfileId)
            || string.IsNullOrWhiteSpace(request.Provenance.InstructionsId) || request.Proposal.Id == Guid.Empty || request.Proposal.TurnId == Guid.Empty
            || request.Proposal.Capability is not { Id: BusinessFormCatalogAssistantCapabilitySource.ProposeId, Version: 1 }
            || request.Proposal.Payload is not FormCatalogProposalPayload payload) return FormCatalogErrors.ContentInvalid;
        var reads = request.Proposal.ObservedReads.Where(read => read.TurnId == request.Proposal.TurnId && read.Ordinal == payload.MetadataReadOrdinal
            && read.Capability is { Id: BusinessFormCatalogAssistantCapabilitySource.MetadataId, Version: 1 }).ToArray();
        if (reads.Length != 1 || reads[0].Payload is not FormCatalogMetadataPayload metadata || !FormCatalogAssistantCodec.ValidMetadata(metadata))
            return FormCatalogErrors.ContentInvalid;
        var content = FormCatalogProposalPolicy.Validate(payload, metadata, request.Context.Culture);
        return content.IsFailure ? content.Error : payload;
    }

    private static StructuredCatalogProposalAcceptedResponse Receipt(BusinessFormCatalog catalog, FormCatalogProposalEvidence evidence) =>
        new(catalog.Id, catalog.ProposalState!.Value.ToString(), evidence.OriginalProposal.Name, evidence.OriginalCulture, evidence.PreviewTotalCount,
            evidence.OriginalProposal.Limitations.Select(item => new StructuredCatalogProposalLimitation(item.Code, item.Criterion)).ToArray());
}
