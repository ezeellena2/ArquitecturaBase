using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantAcceptancePolicy(WhatsAppFormSendPolicy sending, IStructuredFormSource forms,
    IWhatsAppFlowSessionRepository sessions, IWhatsAppConversationRepository conversations, WhatsAppFlowSessionIssuer issuer, TimeProvider clock, WhatsAppAssistantPresentationPolicy presentation,
    WhatsAppAssistantHandoffPolicy handoffs)
{
    /// <summary>
    /// Task 9 of the documents: the form prefilled with what a received document says goes out with the reply's text, in the caller's
    /// transaction (contact then request locked). The session seals the origin (the proposal that recognized it, what was read and the
    /// inbox's document) and what the server adds on capture: the document's own copy for its question, unless the person chooses
    /// another, and the marks. It expires when the inbox stops waiting for the person. Not an offer: it answers what the person sent.
    /// </summary>
    public async Task<Result> AcceptDocumentAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, WhatsAppAssistantTerminalResponse terminal,
        CancellationToken ct)
    {
        if (terminal is not { Send: { } send, Body: { } body }) return WhatsAppAssistantErrors.StateConflict;
        var destination = sending.ValidateContact(contact);
        if (destination.IsFailure) return destination.Error;
        var revalidated = await sending.RevalidateProposedAsync(send.Context, null, ct);
        if (revalidated.IsFailure) return revalidated.Error;
        var prefill = send.Prefill;
        if (prefill.Form.DefinitionId != send.Context.Form.DefinitionId || prefill.Form.VersionId != send.Context.Form.VersionId
            || prefill.Culture != send.Context.Binding.Culture) return WhatsAppAssistantErrors.StateConflict;
        var wire = WhatsAppFlowPrefillPolicy.Build(send.Context.Projection, prefill.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
        if (wire.IsFailure) return wire.Error;
        // Review of task 9: it answers what the person sent, so an outgoing not resolved yet (another reply, an uncertain send) never drops
        // it: it waits its turn in the same queue.
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (send.ExpiresAtUtc <= nowUtc || !row.Ready(terminal.ProposalId, prefill.Form.DefinitionId, nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Assistant, null, send.Reason, send.Proposal,
            prefill.Fields.Select(field => new StructuredRecordProposedValue(field.FieldId, field.Type, field.Value)).ToArray(),
            prefill.Fields.Select(field => field.FieldId).ToArray()) { Document = send.DocumentId };
        var content = new WhatsAppFlowAssistantContent(1, origin, prefill.Fields.Select(field => new WhatsAppAssistantPrefillResponse(field.FieldId, field.Label, field.DisplayValue)).ToArray())
        {
            Server = new(send.File, send.Marks.Count == 0 ? null : send.Marks),
        };
        var fingerprint = DocumentFingerprint(row.RequestId, terminal.ProposalId, send.DocumentId);
        var issue = issuer.CreateProposed(send.Context, row.RequestId, row.ModeRevision, null, fingerprint,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), content, row.ContentExpiresAtUtc, prefill.Catalogs, send.ExpiresAtUtc);
        var formName = prefill.Form.Name[prefill.Culture];
        var opened = WhatsAppFlowLocationPolicy.OpenAssistant(send.Context.Projection, issue.Session, row.RequestId, body, formName, issue.CreatedAtUtc,
            contact.LinkRevision, contact.WaId!, row.ModeRevision);
        if (!row.Accept(opened[0].Id, fingerprint, null, nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        sessions.Add(issue.Session);
        foreach (var message in opened) conversations.Add(message);
        return Result.Success();
    }

    /// <summary>Task 9: a document whose form cannot be sent to the person (CE4-A) is said and the conversation goes to a person.</summary>
    public Task<WhatsAppOutgoingMessage?> HandOffAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, WhatsAppAssistantDocumentHandoff handoff,
        string message, CancellationToken ct) =>
        handoffs.HandOffAsync(contact, row.SubjectUserId, row.RequestId, handoff.ReasonCode, handoff.Reason, message, ct);

    private static string DocumentFingerprint(Guid requestId, Guid proposalId, Guid documentId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format = 1, Kind = "document", RequestId = requestId, ProposalId = proposalId, DocumentId = documentId });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public Task<Result<WhatsAppFormSendContext>> ResolveAsync(Guid contactId, WhatsAppAssistantSuggestionView view, Guid? actor, CancellationToken ct) =>
        view.Prefill is {} prefill ? sending.ResolveProposedAsync(contactId,prefill,actor,ct)
            : Task.FromResult(Result.Failure<WhatsAppFormSendContext>(WhatsAppAssistantErrors.StateConflict));

    // The caller owns the one transaction and has already locked contact then request.
    public async Task<Result<WhatsAppOutgoingMessageResponse>> AcceptAsync(WhatsAppContact contact, WhatsAppAssistantRequest row,
        SendWhatsAppAssistantSuggestionRequest request, WhatsAppAssistantSuggestionView view, Guid actor,
        WhatsAppFormSendContext context, CancellationToken ct)
        => await AcceptCoreAsync(contact,row,request,view,actor,context,ct);

    public Task<Result<WhatsAppOutgoingMessageResponse>> AcceptAutomaticAsync(WhatsAppContact contact, WhatsAppAssistantRequest row,
        WhatsAppAssistantSuggestionView view, WhatsAppFormSendContext context, CancellationToken ct) =>
        AcceptCoreAsync(contact,row,new(contact.Id,row.RequestId,row.QuestionOutgoingId.HasValue?Guid.CreateVersion7():row.RequestId,view.Prefill!.Fields.Select(field=>field.FieldId).ToArray()),view,null,context,ct);

    private async Task<Result<WhatsAppOutgoingMessageResponse>> AcceptCoreAsync(WhatsAppContact contact, WhatsAppAssistantRequest row,
        SendWhatsAppAssistantSuggestionRequest request, WhatsAppAssistantSuggestionView view, Guid? actor,
        WhatsAppFormSendContext context, CancellationToken ct)
    {
        var destination = sending.ValidateContact(contact);
        if (destination.IsFailure) return destination.Error;
        var revalidated = await sending.RevalidateProposedAsync(context,actor,ct);
        if (revalidated.IsFailure) return revalidated.Error;
        var proposal=view.Operation?.Proposal;
        var provenance=view.Operation?.Provenance;
        if(view.Prefill is not {} preview || !row.IsAlways && (proposal is null || provenance is null)) return WhatsAppAssistantErrors.StateConflict;
        StructuredFormPrefillResponse prefill;
        IReadOnlyList<StructuredFormPrefillField> proposedFields;
        StructuredRecordSuggestionOrigin? suggestionOrigin=null;
        if(row.SourceRecordId is {} sourceRecordId)
        {
            var captureCulture=row.CapturedCulture??WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture;
            var configured=await forms.PrepareSuggestionAsync(new(row.SubjectUserId,captureCulture,sourceRecordId,row.SuggestionSetId,row.SuggestionId,
                request.IncludedFieldIds,proposal,true){ActorUserId=actor},ct);
            if(configured.IsFailure)return configured.Error;
            if(actor is null && configured.Value.CreatesAutomaticLoop)return WhatsAppAssistantErrors.StateConflict;
            prefill=configured.Value.Prefill;proposedFields=configured.Value.ProposedFields;suggestionOrigin=configured.Value.OriginSuggestion;
            if(!row.SetCapturedSuggestion(sourceRecordId,suggestionOrigin.SuggestionSetId,suggestionOrigin.OriginSuggestionId,captureCulture,row.IsAlways,clock.GetUtcNow().UtcDateTime))
                return WhatsAppAssistantErrors.StateConflict;
        }
        else if(proposal!.Capability is {Id:"forms.clarify-registration",Version:1})
        {
            var clarificationSelection=await forms.ValidateClarificationAsync(new(row.SubjectUserId,
                WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,proposal)
                {SelectedDefinitionId=preview.Form.DefinitionId,ForSending=true},ct);
            if(clarificationSelection.IsFailure) return clarificationSelection.Error;
            if(clarificationSelection.Value.SelectedForm is not {} selectedForm) return WhatsAppAssistantErrors.StateConflict;
            prefill=selectedForm;proposedFields=prefill.Fields;
        }
        else
        {
            if(proposal is null)return WhatsAppAssistantErrors.StateConflict;
            var prepared=await forms.ValidatePrefillAsync(new(row.SubjectUserId,WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,
                proposal,ForSending:true),ct);
            if(prepared.IsFailure) return prepared.Error;
            prefill=prepared.Value;proposedFields=prefill.Fields;
        }
        if (prefill.DefinitionRevision != preview.DefinitionRevision || prefill.Form.DefinitionId != context.Form.DefinitionId
            || prefill.Form.VersionId != context.Form.VersionId || prefill.Culture != context.Binding.Culture)
            return WhatsAppAssistantErrors.StateConflict;
        if (request.IncludedFieldIds.Distinct().Count() != request.IncludedFieldIds.Count
            || request.IncludedFieldIds.Any(id=>!proposedFields.Any(field=>field.FieldId==id))) return WhatsAppAssistantErrors.InvalidRequest;
        var selected = prefill.Fields.Where(field=>request.IncludedFieldIds.Contains(field.FieldId)).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal);
        var wire = WhatsAppFlowPrefillPolicy.Build(context.Projection,selected);
        if (wire.IsFailure) return wire.Error;
        if (actor is {} sender && await conversations.GetByRequestAsync(contact.Id,sender,request.RequestId,ct) is not null) return WhatsAppChatErrors.RequestConflict;
        if (await conversations.ExistsUnresolvedOutgoingAsync(contact.Id,ct)) return WhatsAppChatErrors.SendPending;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if(row.QuestionOutgoingId.HasValue && row.DefinitionId is null && !row.SelectDefinition(prefill.Form.DefinitionId,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        if (!row.Ready(proposal?.Id,prefill.Form.DefinitionId,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        var included = request.IncludedFieldIds.ToArray();
        var origin = new StructuredRecordCaptureOrigin(actor.HasValue ? StructuredRecordSenderKind.Manual : StructuredRecordSenderKind.Assistant,actor,prefill.Reason,
            proposal is not null && provenance is not null?new(proposal.Id,proposal.Capability.Id,proposal.Capability.Version,provenance):null,
            proposedFields.Select(field=>new StructuredRecordProposedValue(field.FieldId,field.Type,field.Value)).ToArray(),included,suggestionOrigin);
        var content = new WhatsAppFlowAssistantContent(1,origin,proposedFields.Select(field=>new WhatsAppAssistantPrefillResponse(field.FieldId,field.Label,field.DisplayValue)).ToArray());
        var fingerprint = Fingerprint(request,actor,proposal?.Id);
        var issue = issuer.CreateProposed(context,request.RequestId,row.ModeRevision,actor,fingerprint,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),content,row.ContentExpiresAtUtc,prefill.Catalogs);
        var body = DisplayNumbers.Format(prefill.Culture,AssistantTexts.Get("CompleteForm",prefill.Culture),prefill.Form.Name[prefill.Culture]);
        // W1-A: a form with a location opens with its request, the operator's or the assistant's, and its Flow goes out with the
        // location; the reference keeps the first message of the send.
        var formName = prefill.Form.Name[prefill.Culture];
        var opened = actor is {} operatorId
            ? WhatsAppFlowLocationPolicy.Open(context.Projection,issue.Session,operatorId,request.RequestId,body,formName,issue.CreatedAtUtc)
            : WhatsAppFlowLocationPolicy.OpenAssistant(context.Projection,issue.Session,request.RequestId,body,formName,issue.CreatedAtUtc,
                contact.LinkRevision,contact.WaId!,row.ModeRevision);
        var outgoing = opened[0];
        if (!row.Accept(outgoing.Id,fingerprint,actor,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        sessions.Add(issue.Session);foreach(var message in opened)conversations.Add(message);
        var explanation = actor is {} viewer ? await presentation.ReadAsync(viewer,WhatsAppAssistantPresentationPolicy.Row(issue.Session),ct) : null;
        return WhatsAppOutgoingPresentationPolicy.Map(outgoing,issue.Session,explanation);
    }
    private static string Fingerprint(SendWhatsAppAssistantSuggestionRequest request,Guid? actor,Guid? proposalId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format=1,request.ContactId,request.SuggestionRequestId,request.RequestId,Actor=actor,ProposalId=proposalId,Fields=request.IncludedFieldIds.Order().ToArray() });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
