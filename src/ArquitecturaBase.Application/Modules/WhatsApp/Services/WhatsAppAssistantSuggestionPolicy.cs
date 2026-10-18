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
    public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid sourceMessageId,CancellationToken ct) => reader.FindSourceAsync(contactId,sourceMessageId,ct);
    public Task<Result> CancelAsync(WhatsAppAssistantReferenceRow reference, CancellationToken ct) =>
        reference.IsAlways ? Task.FromResult(Result.Success()) : operations.CancelAsync(Context(reference), reference.RequestId, ct);
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
        if(reference.IsAlways) return await ReadConfiguredAsync(reference,viewerUserId,ct);
        var result = await operations.GetAsync(Context(reference), reference.RequestId, ct);
        return await ObserveAsync(reference,result,ct,viewerUserId:viewerUserId);
    }

    public async Task<Result<WhatsAppAssistantSuggestionView>> ObserveAsync(WhatsAppAssistantReferenceRow reference,Result<AssistantOperationResponse> result,CancellationToken ct,Guid? selectedDefinitionId=null,Guid? viewerUserId=null)
    {
        if(reference.ContentExpiresAtUtc<=NowUtc) return View(reference,WhatsAppAssistantRequestState.Blocked);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound || result.Error.Metadata?.TryGetValue("retryable", out var retryable) == true && retryable is true) return View(reference, WhatsAppAssistantRequestState.Pending);
            return result.Error;
        }
        var operation = result.Value;
        if (operation.State is "pending" or "running") return View(reference, WhatsAppAssistantRequestState.Pending, operation);
        if (operation.State != "completed" || operation.Provenance is null || operation.Proposal is not { } proposal)
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        if (proposal.Capability is { Id: "whatsapp.no-reply", Version: 1 }
            && proposal.Payload is WhatsAppAssistantNoReplyPayload { Reason: "not_form" or "operator_needed" })
            return View(reference, WhatsAppAssistantRequestState.NoReply, operation);
        if(proposal.Capability is {Id:"whatsapp.decline-form",Version:1} && proposal.Payload is WhatsAppAssistantDeclinePayload decline)
        {
            var read=proposal.ObservedReads.SingleOrDefault(item=>item.TurnId==proposal.TurnId && item.Ordinal==decline.ReadOrdinal
                && item.Capability==new AssistantCapabilityReference("whatsapp.offered-forms",1));
            if(read?.Payload is not StructuredFormOffersPayload offers || offers.SubjectUserId!=reference.SubjectUserId
                || offers.Culture!=Context(reference).Culture
                || !offers.Offers.Any(offer=>offer.RequestId==decline.RequestId && offer.DefinitionId==decline.DefinitionId))
                return View(reference,WhatsAppAssistantRequestState.Blocked,operation);
            return View(reference,WhatsAppAssistantRequestState.NoReply,operation) with {DeclinedDefinitionId=decline.DefinitionId};
        }
        if(reference.Kind==WhatsAppAssistantRequestKind.Automatic && proposal.Capability is {Id:"forms.clarify-registration",Version:1})
        {
            var clarification=await forms.ValidateClarificationAsync(new(reference.SubjectUserId,Context(reference).Culture,proposal){SelectedDefinitionId=selectedDefinitionId},ct);
            if(clarification.IsFailure) return View(reference,WhatsAppAssistantRequestState.Blocked,operation);
            return new WhatsAppAssistantSuggestionView(new(reference.RequestId,reference.SourceMessageId,WhatsAppAssistantRequestState.Ready,null,null),operation,clarification.Value.SelectedForm,clarification.Value);
        }
        if(reference.SourceRecordId is {} recordId && proposal.Capability is {Id:"forms.propose-suggestion",Version:1})
        {
            var suggestion=await forms.PrepareSuggestionAsync(new(reference.SubjectUserId,Context(reference).Culture,recordId,
                reference.SuggestionSetId,reference.SuggestionId,Proposal:proposal){ActorUserId=reference.Kind==WhatsAppAssistantRequestKind.Manual?viewerUserId??reference.ActorUserId:null},ct);
            if(suggestion.IsFailure) return suggestion.Error;
            return Prepared(reference,proposal.Id,suggestion.Value.Prefill,operation,suggestion.Value);
        }
        if (proposal.Capability is not { Id: "forms.propose-registration", Version: 1 })
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        var prepared = await forms.ValidatePrefillAsync(new(reference.SubjectUserId, Context(reference).Culture, proposal), ct);
        if (prepared.IsFailure)
        {
            if (prepared.Error.Type is ErrorType.Unauthorized or ErrorType.Forbidden) return prepared.Error;
            return View(reference, WhatsAppAssistantRequestState.Blocked, operation);
        }
        return Prepared(reference,proposal.Id,prepared.Value,operation);
    }

    public Task<Result<StructuredFormSuggestionsResponse>> ListConfiguredAsync(Guid subject,string culture,Guid recordId,CancellationToken ct)=>
        forms.ListSuggestionsAsync(subject,culture,recordId,ct);

    public async Task<Result<WhatsAppAssistantSuggestionView>> ReadConfiguredAsync(WhatsAppAssistantReferenceRow reference,Guid? actor,CancellationToken ct)
    {
        if(!reference.IsAlways || reference.SourceRecordId is not {} record || reference.SuggestionSetId is not {} set
            || reference.SuggestionId is not {} suggestion || reference.CapturedCulture is not ("es" or "en")) return WhatsAppAssistantErrors.StateConflict;
        var prepared=await forms.PrepareSuggestionAsync(new(reference.SubjectUserId,reference.CapturedCulture,record,set,suggestion){ActorUserId=actor},ct);
        return prepared.IsFailure?prepared.Error:Prepared(reference,null,prepared.Value.Prefill,null,prepared.Value);
    }

    private static Result<WhatsAppAssistantSuggestionView> Prepared(WhatsAppAssistantReferenceRow reference,Guid? proposalId,StructuredFormPrefillResponse prefill,
        AssistantOperationResponse? operation,StructuredFormSuggestionPreparationResponse? suggestion=null)
    {
        if(!prefill.Form.Name.TryGetValue(prefill.Culture,out var name))return View(reference,WhatsAppAssistantRequestState.Blocked,operation);
        var response=new WhatsAppAssistantFormProposalResponse(proposalId,prefill.Form.DefinitionId,prefill.Form.VersionId,
            checked((int)prefill.Form.VersionNumber),name,prefill.Culture,prefill.Reason,
            (suggestion?.ProposedFields??prefill.Fields).Select(field=>new WhatsAppAssistantPrefillResponse(field.FieldId,field.Label,field.DisplayValue)).ToArray());
        return new WhatsAppAssistantSuggestionView(new(reference.RequestId,reference.SourceMessageId,WhatsAppAssistantRequestState.Ready,response,null),operation,prefill,Suggestion:suggestion);
    }

    private static WhatsAppAssistantSuggestionView View(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantRequestState state,
        AssistantOperationResponse? operation = null) => new(new(reference.RequestId, reference.SourceMessageId, state, null, null), operation, null);

    public static AssistantChannelContext Context(WhatsAppAssistantReferenceRow row) => JsonSerializer.Deserialize<AssistantChannelContext>(row.ContextJson ?? throw new InvalidOperationException("A configured reference has no model context."), JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The saved channel context is invalid.");
    public static WhatsAppAssistantReferenceRow Reference(WhatsAppAssistantRequest row) => new(row.RequestId, row.ContactId, row.SourceMessageId, row.ActorUserId, row.SubjectUserId,
        row.Kind, row.ModeRevision, row.LinkRevision, row.ContextJson, row.ContentExpiresAtUtc, row.State, row.ProposalId, row.DefinitionId, row.OutgoingId) { QuestionOutgoingId=row.QuestionOutgoingId,SourceRecordId=row.SourceRecordId,SuggestionSetId=row.SuggestionSetId,SuggestionId=row.SuggestionId,CapturedCulture=row.CapturedCulture,IsAlways=row.IsAlways };
}
internal sealed record WhatsAppAssistantSuggestionView(WhatsAppAssistantSuggestionResponse Response, AssistantOperationResponse? Operation, StructuredFormPrefillResponse? Prefill, StructuredFormClarificationResponse? Clarification=null,Guid? DeclinedDefinitionId=null,StructuredFormSuggestionPreparationResponse? Suggestion=null);
