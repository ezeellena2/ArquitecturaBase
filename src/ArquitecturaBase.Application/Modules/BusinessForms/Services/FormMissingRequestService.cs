using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormMissingRequestService(IFormMissingRequestRepository repository,IFormMissingRequestReader reader,
    IUserReader users,IPermissionService permissions,ICurrentUser currentUser,IUnitOfWork unitOfWork,TimeProvider timeProvider,
    IRequestValidator validator) : IFormMissingRequestService,IStructuredMissingFormRequestSink
{
    public async Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request,CancellationToken ct)
    {
        var proposal = ValidateProposal(request);
        if (proposal.IsFailure) return proposal.Error;
        var access = await CheckCaptureAsync(request,ct);
        if (access.IsFailure) return access.Error;
        return await unitOfWork.ExecuteInTransactionAsync<Result<StructuredMissingFormRequestCaptureResponse>>(async inner =>
        {
            await repository.LockAsync(inner);
            var current = await CheckCaptureAsync(request,inner);
            if (current.IsFailure) return current.Error;
            if (await repository.GetRequestAsync(request.SourceReceiptId,inner) is { } prior)
                return prior.SubjectUserId == request.Context.SubjectUserId && prior.RequestedAtUtc == request.RequestedAtUtc
                    ? new StructuredMissingFormRequestCaptureResponse(prior.Id,prior.TopicId,true) : FormMissingRequestErrors.ContentInvalid;
            var value = proposal.Value;
            var topic = value.ExistingTopicId is { } id ? await repository.GetAsync(id,inner)
                : await repository.GetByNameAsync(BusinessFormMissingTopic.NormalizeName(value.Name!),inner);
            if (value.ExistingTopicId.HasValue && topic is null) return FormMissingRequestErrors.ContentInvalid;
            var isNew = topic is null;
            if (topic is null)
            {
                var created = BusinessFormMissingTopic.Create(value.Name!,value.Description!,request.Context.Culture);
                if (created.IsFailure) return created.Error;
                topic = created.Value;
            }
            else
            {
                var registered = topic.RegisterRequest();
                if (registered.IsFailure) return registered.Error;
            }
            var captured = BusinessFormMissingRequest.Create(request.SourceReceiptId,topic.Id,request.Context.SubjectUserId!.Value,request.RequestedAtUtc);
            if (captured.IsFailure) return captured.Error;
            if (isNew) repository.Add(topic);
            repository.Add(captured.Value);
            return new StructuredMissingFormRequestCaptureResponse(captured.Value.Id,topic.Id,false);
        },CommitPolicy.OnSuccess,ct);
    }

    public async Task<Result<MissingFormTopicsPageResponse>> ListAsync(ListMissingFormTopicsRequest request,CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await CanManageAsync(actor,ct)) return FormDefinitionErrors.Forbidden;
        if (await validator.ValidateAsync(request,ct) is { } invalid) return invalid;
        var page = await reader.FindPageAsync(request,ct);
        return await CanManageAsync(actor,ct) ? page : FormDefinitionErrors.Forbidden;
    }

    public async Task<Result<MissingFormTopicResponse>> DiscardAsync(Guid topicId,DiscardMissingFormTopicRequest request,CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await CanManageAsync(actor,ct)) return FormDefinitionErrors.Forbidden;
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991) return FieldErrors.On(FormMissingRequestErrors.ContentInvalid,"expectedRevision");
        return await unitOfWork.ExecuteInTransactionAsync<Result<MissingFormTopicResponse>>(async inner =>
        {
            await repository.LockAsync(inner);
            if (!await CanManageAsync(actor,inner)) return FormDefinitionErrors.Forbidden;
            var topic = await repository.GetAsync(topicId,inner);
            if (topic is null) return FormMissingRequestErrors.NotFound;
            var row = await reader.FindAsync(topicId,inner) ?? throw new InvalidOperationException("The missing topic aggregate is missing.");
            if (!await CanManageAsync(actor,inner)) return FormDefinitionErrors.Forbidden;
            var account = await users.FindByIdAsync(actor,inner);
            if (account is not { IsActive: true }) return FormDefinitionErrors.Forbidden;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var discarded = topic.Discard(request.ExpectedRevision.Value,actor,now);
            if (discarded.IsFailure) return discarded.Error;
            return row with { Revision = topic.Revision, State = topic.State.ToString(),DiscardedBy = new(actor,account.DisplayName),DiscardedAtUtc = now };
        },CommitPolicy.OnSuccess,ct);
    }

    private async Task<bool> CanManageAsync(Guid actor,CancellationToken ct) =>
        await users.FindByIdAsync(actor,ct) is { IsActive: true } && await permissions.HasPermissionAsync(actor,BusinessFormsPermissions.Manage,ct);

    private async Task<Result> CheckCaptureAsync(StructuredMissingFormRequestCaptureRequest request,CancellationToken ct)
    {
        if (await users.FindByIdAsync(request.Context.SubjectUserId!.Value,ct) is not { IsActive: true } subject
            || subject.Culture != request.Context.Culture
            || await users.FindByIdAsync(request.Context.ActorUserId,ct) is not { IsActive: true } actor) return FormDefinitionErrors.Forbidden;
        foreach (var permission in request.Context.RequiredPermissions.Actor)
            if (!await permissions.HasPermissionAsync(actor.Id,permission,ct)) return FormDefinitionErrors.Forbidden;
        foreach (var permission in request.Context.RequiredPermissions.Subject)
            if (!await permissions.HasPermissionAsync(subject.Id,permission,ct)) return FormDefinitionErrors.Forbidden;
        var payload = (MissingFormRequestProposalPayload)request.Proposal.Payload;
        return FormMissingRequestPrivacyPolicy.HasIdentifier(payload.Name,payload.Description,subject)
            || FormMissingRequestPrivacyPolicy.HasIdentifier(payload.Name,payload.Description,actor) ? FormMissingRequestErrors.ContentInvalid : Result.Success();
    }

    private Result<MissingFormRequestProposalPayload> ValidateProposal(StructuredMissingFormRequestCaptureRequest request)
    {
        if (request.SourceReceiptId == Guid.Empty || request.RequestedAtUtc.Kind != DateTimeKind.Utc || request.RequestedAtUtc > timeProvider.GetUtcNow().UtcDateTime
            || request.Context.SubjectUserId is not { } subject || subject == Guid.Empty || request.Context.ActorUserId == Guid.Empty
            || request.Context.ChannelId is not ("whatsapp-assistant" or "whatsapp-manual")
            || request.Context.ChannelId == "whatsapp-assistant" && request.Context.ActorUserId != subject
            || request.Context.Culture is not ("es" or "en") || request.Provenance.ChannelId != request.Context.ChannelId
            || request.Provenance.OccurredAtUtc.Kind != DateTimeKind.Utc || request.Provenance.ModelProfileVersion < 1 || request.Provenance.InstructionsVersion < 1
            || string.IsNullOrWhiteSpace(request.Provenance.ModelProfileId) || string.IsNullOrWhiteSpace(request.Provenance.InstructionsId)
            || request.Proposal.Id == Guid.Empty || request.Proposal.TurnId == Guid.Empty
            || request.Proposal.Capability is not { Id: BusinessFormMissingRequestAssistantCapabilitySource.ProposeId,Version: 1 }
            || request.Proposal.Payload is not MissingFormRequestProposalPayload payload || payload.State != "proposed"
            || !FormMissingRequestAssistantCodec.Valid(payload.ExistingTopicId,payload.Name,payload.Description)
            || FormMissingRequestPrivacyPolicy.HasContactDetails(payload.Name, payload.Description)) return FormMissingRequestErrors.ContentInvalid;
        var observed = FormMissingRequestAssistantCodec.Find(request.Proposal.ObservedReads,request.Proposal.TurnId,subject,request.Context.Culture);
        if (observed.IsFailure || observed.Value.ReadOrdinal != payload.ReadOrdinal
            || payload.ExistingTopicId is { } topicId && !observed.Value.Snapshot.Topics.Any(topic => topic.Id == topicId)) return FormMissingRequestErrors.ContentInvalid;
        return payload;
    }
}
