using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantAcceptancePolicy(WhatsAppFormSendPolicy sending, IStructuredFormSource forms,
    IWhatsAppFlowSessionRepository sessions, IWhatsAppConversationRepository conversations, WhatsAppFlowSessionIssuer issuer, TimeProvider clock, WhatsAppAssistantPresentationPolicy presentation)
{
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
        if (view.Operation is not { Proposal: {} proposal, Provenance: {} provenance } || view.Prefill is not {} preview)
            return WhatsAppAssistantErrors.StateConflict;
        StructuredFormPrefillResponse prefill;
        if(proposal.Capability is {Id:"forms.clarify-registration",Version:1})
        {
            var clarificationSelection=await forms.ValidateClarificationAsync(new(row.SubjectUserId,
                WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,proposal)
                {SelectedDefinitionId=preview.Form.DefinitionId,ForSending=true},ct);
            if(clarificationSelection.IsFailure) return clarificationSelection.Error;
            if(clarificationSelection.Value.SelectedForm is not {} selectedForm) return WhatsAppAssistantErrors.StateConflict;
            prefill=selectedForm;
        }
        else
        {
            var prepared=await forms.ValidatePrefillAsync(new(row.SubjectUserId,WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,
                proposal,ForSending:true),ct);
            if(prepared.IsFailure) return prepared.Error;
            prefill=prepared.Value;
        }
        if (prefill.DefinitionRevision != preview.DefinitionRevision || prefill.Form.DefinitionId != context.Form.DefinitionId
            || prefill.Form.VersionId != context.Form.VersionId || prefill.Culture != context.Binding.Culture)
            return WhatsAppAssistantErrors.StateConflict;
        if (request.IncludedFieldIds.Distinct().Count() != request.IncludedFieldIds.Count
            || request.IncludedFieldIds.Any(id=>!prefill.Fields.Any(field=>field.FieldId==id))) return WhatsAppAssistantErrors.InvalidRequest;
        var selected = prefill.Fields.Where(field=>request.IncludedFieldIds.Contains(field.FieldId)).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal);
        var wire = WhatsAppFlowPrefillPolicy.Build(context.Projection,selected);
        if (wire.IsFailure) return wire.Error;
        if (actor is {} sender && await conversations.GetByRequestAsync(contact.Id,sender,request.RequestId,ct) is not null) return WhatsAppChatErrors.RequestConflict;
        if (await conversations.ExistsUnresolvedOutgoingAsync(contact.Id,ct)) return WhatsAppChatErrors.SendPending;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if(row.QuestionOutgoingId.HasValue && row.DefinitionId is null && !row.SelectDefinition(prefill.Form.DefinitionId,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        if (!row.Ready(proposal.Id,prefill.Form.DefinitionId,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        var included = request.IncludedFieldIds.ToArray();
        var origin = new StructuredRecordCaptureOrigin(actor.HasValue ? StructuredRecordSenderKind.Manual : StructuredRecordSenderKind.Assistant,actor,prefill.Reason,
            new(proposal.Id,proposal.Capability.Id,proposal.Capability.Version,provenance),
            prefill.Fields.Select(field=>new StructuredRecordProposedValue(field.FieldId,field.Type,field.Value)).ToArray(),included);
        var content = new WhatsAppFlowAssistantContent(1,origin,prefill.Fields.Select(field=>new WhatsAppAssistantPrefillResponse(field.FieldId,field.Label,field.DisplayValue)).ToArray());
        var fingerprint = Fingerprint(request,actor,proposal.Id);
        var issue = issuer.CreateProposed(context,request.RequestId,row.ModeRevision,actor,fingerprint,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),content,row.ContentExpiresAtUtc,prefill.Catalogs);
        var body = AssistantTexts.Get("CompleteForm",prefill.Culture);
        var outgoing = actor is {} operatorId
            ? WhatsAppOutgoingMessage.CreateFlow(operatorId,contact.Id,request.RequestId,body,issue.Session.Id,issue.CreatedAtUtc)
            : WhatsAppOutgoingMessage.CreateAssistantFlow(row.SubjectUserId,contact.Id,request.RequestId,body,issue.Session.Id,issue.CreatedAtUtc,
                contact.LinkRevision,contact.WaId!,row.ModeRevision);
        if (!row.Accept(outgoing.Id,fingerprint,actor,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        outgoing.AttachFlowSession(issue.Session);sessions.Add(issue.Session);conversations.Add(outgoing);
        var explanation = actor is {} viewer ? await presentation.ReadAsync(viewer,WhatsAppAssistantPresentationPolicy.Row(issue.Session),ct) : null;
        return WhatsAppOutgoingPresentationPolicy.Map(outgoing,issue.Session,explanation);
    }
    private static string Fingerprint(SendWhatsAppAssistantSuggestionRequest request,Guid? actor,Guid proposalId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format=1,request.ContactId,request.SuggestionRequestId,request.RequestId,Actor=actor,ProposalId=proposalId,Fields=request.IncludedFieldIds.Order().ToArray() });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
