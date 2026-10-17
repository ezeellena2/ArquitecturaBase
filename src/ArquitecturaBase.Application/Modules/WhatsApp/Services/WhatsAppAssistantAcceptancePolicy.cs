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
    public Task<Result<WhatsAppFormSendContext>> ResolveAsync(Guid contactId, WhatsAppAssistantSuggestionView view, Guid actor, CancellationToken ct) =>
        view.Prefill is {} prefill ? sending.ResolveProposedAsync(contactId,prefill,actor,ct)
            : Task.FromResult(Result.Failure<WhatsAppFormSendContext>(WhatsAppAssistantErrors.StateConflict));

    // The caller owns the one transaction and has already locked contact then request.
    public async Task<Result<WhatsAppOutgoingMessageResponse>> AcceptAsync(WhatsAppContact contact, WhatsAppAssistantRequest row,
        SendWhatsAppAssistantSuggestionRequest request, WhatsAppAssistantSuggestionView view, Guid actor,
        WhatsAppFormSendContext context, CancellationToken ct)
    {
        var destination = sending.ValidateContact(contact);
        if (destination.IsFailure) return destination.Error;
        var revalidated = await sending.RevalidateProposedAsync(context,actor,ct);
        if (revalidated.IsFailure) return revalidated.Error;
        if (view.Operation is not { Proposal: {} proposal, Provenance: {} provenance } || view.Prefill is not {} preview)
            return WhatsAppAssistantErrors.StateConflict;
        var prepared = await forms.ValidatePrefillAsync(new(row.SubjectUserId,WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,
            proposal,ForSending:true),ct);
        if (prepared.IsFailure) return prepared.Error;
        var prefill = prepared.Value;
        if (prefill.DefinitionRevision != preview.DefinitionRevision || prefill.Form.DefinitionId != context.Form.DefinitionId
            || prefill.Form.VersionId != context.Form.VersionId || prefill.Culture != context.Binding.Culture)
            return WhatsAppAssistantErrors.StateConflict;
        if (request.IncludedFieldIds.Distinct().Count() != request.IncludedFieldIds.Count
            || request.IncludedFieldIds.Any(id=>!prefill.Fields.Any(field=>field.FieldId==id))) return WhatsAppAssistantErrors.InvalidRequest;
        var selected = prefill.Fields.Where(field=>request.IncludedFieldIds.Contains(field.FieldId)).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal);
        var wire = WhatsAppFlowPrefillPolicy.Build(context.Projection,selected);
        if (wire.IsFailure) return wire.Error;
        if (await conversations.GetByRequestAsync(contact.Id,actor,request.RequestId,ct) is not null) return WhatsAppChatErrors.RequestConflict;
        if (await conversations.ExistsUnresolvedOutgoingAsync(contact.Id,ct)) return WhatsAppChatErrors.SendPending;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (!row.Ready(proposal.Id,prefill.Form.DefinitionId,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        var included = request.IncludedFieldIds.ToArray();
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual,actor,prefill.Reason,
            new(proposal.Id,proposal.Capability.Id,proposal.Capability.Version,provenance),
            prefill.Fields.Select(field=>new StructuredRecordProposedValue(field.FieldId,field.Type,field.Value)).ToArray(),included);
        var content = new WhatsAppFlowAssistantContent(1,origin,prefill.Fields.Select(field=>new WhatsAppAssistantPrefillResponse(field.FieldId,field.Label,field.DisplayValue)).ToArray());
        var fingerprint = Fingerprint(request,actor,proposal.Id);
        var issue = issuer.CreateProposed(context,request.RequestId,row.ModeRevision,actor,fingerprint,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),content,row.ContentExpiresAtUtc,prefill.Catalogs);
        var outgoing = WhatsAppOutgoingMessage.CreateFlow(actor,contact.Id,request.RequestId,AssistantTexts.Get("CompleteForm",prefill.Culture),issue.Session.Id,issue.CreatedAtUtc);
        if (!row.Accept(outgoing.Id,fingerprint,actor,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
        outgoing.AttachFlowSession(issue.Session);sessions.Add(issue.Session);conversations.Add(outgoing);
        var explanation = await presentation.ReadAsync(actor,WhatsAppAssistantPresentationPolicy.Row(issue.Session),ct);
        return WhatsAppOutgoingPresentationPolicy.Map(outgoing,issue.Session,explanation);
    }
    private static string Fingerprint(SendWhatsAppAssistantSuggestionRequest request,Guid actor,Guid proposalId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format=1,request.ContactId,request.SuggestionRequestId,request.RequestId,Actor=actor,ProposalId=proposalId,Fields=request.IncludedFieldIds.Order().ToArray() });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
