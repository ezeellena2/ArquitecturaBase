using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// VC1-A (task 29): the notice of the loads of a spreadsheet and its loads, in the caller's transaction (contact then request locked). The
/// first reference keeps the notice, with «Por qué lo propuso»; each load is a reference of its own that carries the spreadsheet
/// (<see cref="WhatsAppAssistantRequest.CreateLoad"/>), with its send: the session seals the origin (the proposal, what was proposed and the
/// row of the inbox) and the marks, and no file, because the spreadsheet is not the receipt. Its Flow carries its body and its footer and
/// goes after the notice, in the order of the spreadsheet. Everything is checked before anything changes: it goes out whole or not at all.
/// Not offers: they answer the file the person sent.
/// </summary>
internal sealed class WhatsAppAssistantLoadAcceptancePolicy(WhatsAppFormSendPolicy sending, IWhatsAppFlowSessionRepository sessions,
    IWhatsAppConversationRepository conversations, IWhatsAppAssistantRepository requests, IWhatsAppAssistantReader reader, WhatsAppFlowSessionIssuer issuer,
    TimeProvider clock)
{
    public async Task<Result> AcceptAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, WhatsAppAssistantTerminalResponse terminal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(terminal);
        if (terminal is not { Loads: { } loads, Body: { } body } || row.Kind != WhatsAppAssistantRequestKind.Automatic || row.ActionOrdinal != 0
            || row.AttachmentMessageId is not { } attachmentMessageId || loads.Count > WhatsAppAssistantRequest.MaxLoads || contact.WaId is not { } waId)
            return WhatsAppAssistantErrors.StateConflict;
        // Answered again from what the inbox kept (review of task 29), it goes only while nothing answered that spreadsheet: another reference
        // that carries it may have sent its notice since it was read. A form chosen from the list continues a question that answered it, so only
        // another continuation of that question counts.
        if (await reader.ExistsAttachmentAnswerAsync(contact.Id, attachmentMessageId, ct, WhatsAppAssistantSpreadsheetPolicy.ContinuedQuestion(
                WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)), row.PreviousRequestId)))
            return row.FinishWithoutReply(false, clock.GetUtcNow().UtcDateTime) ? Result.Success() : WhatsAppAssistantErrors.StateConflict;
        var destination = sending.ValidateContact(contact);
        if (destination.IsFailure) return destination.Error;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        foreach (var load in loads)
        {
            var send = load.Send;
            var revalidated = await sending.RevalidateProposedAsync(send.Context, null, ct);
            if (revalidated.IsFailure) return revalidated.Error;
            var prefill = send.Prefill;
            if (prefill.Form.DefinitionId != send.Context.Form.DefinitionId || prefill.Form.VersionId != send.Context.Form.VersionId
                || prefill.Culture != send.Context.Binding.Culture || send.ExpiresAtUtc <= nowUtc || nowUtc >= row.ContentExpiresAtUtc)
                return WhatsAppAssistantErrors.StateConflict;
            var wire = WhatsAppFlowPrefillPolicy.Build(send.Context.Projection, prefill.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
            if (wire.IsFailure) return wire.Error;
        }
        var notice = WhatsAppOutgoingMessage.CreateAssistantText(row.SubjectUserId, contact.Id, row.RequestId, body, nowUtc, contact.LinkRevision, waId, row.ModeRevision);
        if (terminal.SpreadsheetRowsJson is { } rowsJson && !notice.ExplainSpreadsheetRows(rowsJson)) return WhatsAppAssistantErrors.StateConflict;
        if (!row.Ready(terminal.ProposalId, null, nowUtc) || !row.Accept(notice.Id, Fingerprint(row.RequestId, terminal.ProposalId, null), null, nowUtc))
            return WhatsAppAssistantErrors.StateConflict;
        conversations.Add(notice);
        for (var index = 0; index < loads.Count; index++)
        {
            var send = loads[index].Send;
            var prefill = send.Prefill;
            var reference = WhatsAppAssistantRequest.CreateLoad(row, Guid.CreateVersion7(), index + 1, nowUtc);
            var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Assistant, null, send.Reason, send.Proposal,
                prefill.Fields.Select(field => new StructuredRecordProposedValue(field.FieldId, field.Type, field.Value)).ToArray(),
                prefill.Fields.Select(field => field.FieldId).ToArray()) { Document = send.DocumentId };
            var content = new WhatsAppFlowAssistantContent(1, origin, prefill.Fields.Select(field => new WhatsAppAssistantPrefillResponse(field.FieldId, field.Label, field.DisplayValue)).ToArray())
            {
                Server = new(null, send.Marks.Count == 0 ? null : send.Marks) { Spreadsheet = true },
            };
            var fingerprint = Fingerprint(reference.RequestId, terminal.ProposalId, send.DocumentId);
            var issue = issuer.CreateProposed(send.Context, reference.RequestId, row.ModeRevision, null, fingerprint,
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), content, row.ContentExpiresAtUtc, prefill.Catalogs, send.ExpiresAtUtc);
            // After the notice and in the order of the spreadsheet: one microsecond apart, the precision of the database.
            var opened = WhatsAppFlowLocationPolicy.OpenAssistant(send.Context.Projection, issue.Session, reference.RequestId, loads[index].Body,
                prefill.Form.Name[prefill.Culture], nowUtc.AddTicks(10 * (index + 1)), contact.LinkRevision, waId, row.ModeRevision);
            // After the checks above these cannot fail; if one did, it is a bug and the whole unit of work rolls back.
            if (opened.Where(message => message.Kind == WhatsAppOutgoingMessageKind.Flow).Any(message => !message.AttachFooter(loads[index].Footer))
                || !reference.Ready(terminal.ProposalId, prefill.Form.DefinitionId, nowUtc) || !reference.Accept(opened[0].Id, fingerprint, null, nowUtc))
                throw new InvalidOperationException("A checked load could not be queued.");
            requests.Add(reference);
            sessions.Add(issue.Session);
            foreach (var message in opened) conversations.Add(message);
        }
        return Result.Success();
    }

    private static string Fingerprint(Guid requestId, Guid proposalId, Guid? documentId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Format = 1, Kind = "spreadsheet", RequestId = requestId, ProposalId = proposalId, DocumentId = documentId });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
