using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantSuggestionPolicy(IWhatsAppAssistantReader reader, IAssistantOperations operations,
    IStructuredFormSource forms, TimeProvider clock, WhatsAppAssistantPresentationPolicy presentation)
{
    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;
    public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId, Guid requestId, CancellationToken ct) => reader.FindAsync(contactId, requestId, ct);
    public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId, CancellationToken ct) => reader.FindLatestSourceAsync(contactId, ct);
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct) => operations.GetAvailabilityAsync(ct);

    public async Task<Result<WhatsAppAssistantSuggestionView>> ReadAsync(WhatsAppAssistantReferenceRow reference, CancellationToken ct, Guid? viewerUserId = null)
    {
        if (reference.State is WhatsAppAssistantRequestState.NoReply or WhatsAppAssistantRequestState.Blocked or WhatsAppAssistantRequestState.Discarded)
            return View(reference, reference.State);
        if (reference.State == WhatsAppAssistantRequestState.Sent)
        {
            var sent = await reader.FindOutgoingAsync(reference.ContactId, reference.RequestId, ct);
            if (sent is null) return WhatsAppAssistantErrors.StateConflict;
            var explanation = viewerUserId is {} viewer ? await presentation.ReadAsync(viewer,sent.Assistant,ct) : null;
            var receipt = WhatsAppOutgoingPresentationPolicy.Map(sent,explanation);
            return new WhatsAppAssistantSuggestionView(new(reference.RequestId, reference.SourceMessageId, reference.State, null, receipt), null, null);
        }
        if (reference.ContentExpiresAtUtc <= NowUtc) return View(reference, WhatsAppAssistantRequestState.Blocked);
        if (reference.Kind == WhatsAppAssistantRequestKind.Manual)
        {
            var current = await reader.FindCurrentAsync(reference.ContactId, ct);
            if (current?.RequestId != reference.RequestId || current.SourceMessageId != reference.SourceMessageId)
                return WhatsAppAssistantErrors.StateConflict;
        }
        var result = await operations.GetAsync(Context(reference), reference.RequestId, ct);
        return await ObserveAsync(reference,result,ct);
    }

    public async Task<Result<WhatsAppAssistantSuggestionView>> ObserveAsync(WhatsAppAssistantReferenceRow reference,Result<AssistantOperationResponse> result,CancellationToken ct,Guid? selectedDefinitionId=null)
    {
        if(reference.ContentExpiresAtUtc<=NowUtc) return View(reference,WhatsAppAssistantRequestState.Blocked);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound) return View(reference, WhatsAppAssistantRequestState.Pending);
            return result.Error;
        }
        var operation = result.Value;
        if (operation.State is "pending" or "running") return View(reference, WhatsAppAssistantRequestState.Pending, operation);
        if (operation.State != "completed" || operation.Provenance is null || operation.Proposal is not { } proposal)
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        if (proposal.Capability is { Id: "whatsapp.no-reply", Version: 1 }
            && proposal.Payload is WhatsAppAssistantNoReplyPayload { Reason: "not_form" or "operator_needed" })
            return View(reference, WhatsAppAssistantRequestState.NoReply, operation);
        if(reference.Kind==WhatsAppAssistantRequestKind.Automatic && proposal.Capability is {Id:"forms.clarify-registration",Version:1})
        {
            var clarification=await forms.ValidateClarificationAsync(new(reference.SubjectUserId,Context(reference).Culture,proposal){SelectedDefinitionId=selectedDefinitionId},ct);
            if(clarification.IsFailure) return View(reference,WhatsAppAssistantRequestState.Blocked,operation);
            return new WhatsAppAssistantSuggestionView(new(reference.RequestId,reference.SourceMessageId,WhatsAppAssistantRequestState.Ready,null,null),operation,clarification.Value.SelectedForm,clarification.Value);
        }
        if (proposal.Capability is not { Id: "forms.propose-registration", Version: 1 })
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        var prepared = await forms.ValidatePrefillAsync(new(reference.SubjectUserId, Context(reference).Culture, proposal), ct);
        if (prepared.IsFailure)
        {
            if (prepared.Error.Type is ErrorType.Unauthorized or ErrorType.Forbidden) return prepared.Error;
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        }
        var prefill = prepared.Value;
        if (!prefill.Form.Name.TryGetValue(prefill.Culture, out var name))
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        var response = new WhatsAppAssistantFormProposalResponse(proposal.Id, prefill.Form.DefinitionId, prefill.Form.VersionId,
            checked((int)prefill.Form.VersionNumber), name, prefill.Culture, prefill.Reason,
            prefill.Fields.Select(field => new WhatsAppAssistantPrefillResponse(field.FieldId, field.Label, field.DisplayValue)).ToArray());
        return new WhatsAppAssistantSuggestionView(new(reference.RequestId, reference.SourceMessageId, WhatsAppAssistantRequestState.Ready, response, null), operation, prefill);
    }

    private static WhatsAppAssistantSuggestionView View(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantRequestState state,
        AssistantOperationResponse? operation = null) => new(new(reference.RequestId, reference.SourceMessageId, state, null, null), operation, null);

    public static AssistantChannelContext Context(WhatsAppAssistantReferenceRow row) => JsonSerializer.Deserialize<AssistantChannelContext>(row.ContextJson, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The saved channel context is invalid.");
    public static WhatsAppAssistantReferenceRow Reference(WhatsAppAssistantRequest row) => new(row.RequestId, row.ContactId, row.SourceMessageId, row.ActorUserId, row.SubjectUserId,
        row.Kind, row.ModeRevision, row.LinkRevision, row.ContextJson, row.ContentExpiresAtUtc, row.State, row.ProposalId, row.DefinitionId, row.OutgoingId) { QuestionOutgoingId=row.QuestionOutgoingId };
}
internal sealed record WhatsAppAssistantSuggestionView(WhatsAppAssistantSuggestionResponse Response, AssistantOperationResponse? Operation, StructuredFormPrefillResponse? Prefill, StructuredFormClarificationResponse? Clarification=null);
