using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 9 of the documents: a tap on an option of a document question gets its own reference, carrying the document, and is marked in the
// same commit, so a retry after the commit finds it answered. What it decided outside the transaction (the inbox's port and the send) is
// queued here: the chosen candidate's form, «Otro» or «la pregunta ya cambió»; a form chosen when the document was not recognized leaves a
// pending reference that continues the operation with that form fixed, which the next interpretation takes. VC1-A: so does a form chosen for a
// spreadsheet none of whose rows fit, whose continuation only loads its rows.
internal sealed class WhatsAppAssistantDocumentService(IWhatsAppAssistantReader reader, IWhatsAppAssistantRepository requests,
    IWhatsAppConversationRepository conversations, IUnitOfWork unitOfWork, WhatsAppAssistantAutomaticPolicy automatic,
    WhatsAppAssistantDocumentPolicy documents, TimeProvider clock, ILogger<WhatsAppAssistantDocumentService> logger) : IWhatsAppAssistantDocumentService
{
    public Task<Result> ProcessTapAsync(Guid contactId, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppDocumentTap", async () =>
        {
            var tap = await reader.FindLatestDocumentTapAsync(contactId, ct);
            if (tap is null) return Result.Success();
            var choice = await documents.ChooseAsync(contactId, tap, ct);
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var contact = await conversations.GetForUpdateAsync(contactId, token);
                var current = await reader.FindLatestDocumentTapAsync(contactId, token);
                if (contact is null || current?.Id != tap.Id || current.ReplyId != tap.ReplyId) return Result.Success();
                await automatic.MarkChoiceAsync(tap, contactId, token);
                if (choice is not { Reference: { ActorUserId: { } actor, ContextJson: { } contextJson, AttachmentMessageId: { } attachment } reference }
                    || contact.Mode != WhatsAppConversationMode.Assistant || contact.WaId is null || contact.UserId != reference.SubjectUserId
                    || contact.LinkRevision != reference.LinkRevision
                    || await requests.GetBySourceAsync(contactId, tap.Id, WhatsAppAssistantRequestKind.Automatic, token) is not null)
                    return Result.Success();
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                if (reference.ContentExpiresAtUtc <= nowUtc) return Result.Success();
                // Single use, also under the contact's lock: once another tap on the question got its own reference, this one is told it
                // changed (what the inbox decided outside was already refused before asking it, as the processor takes a contact at a time).
                if (await reader.FindDocumentTapStateAsync(reference, tap.Id, nowUtc, token) == WhatsAppAssistantDocumentTapState.Answered
                    && WhatsAppAssistantDocumentReplyCodec.TryParse(tap.ReplyId, out _, out var answeredProposal, out _))
                    choice = choice with { Reply = WhatsAppAssistantDocumentPolicy.Replaced(reference, answeredProposal), FocusDefinitionId = null, Applies = false };
                if (choice.FocusDefinitionId is { } focus)
                {
                    AssistantChannelContext? context;
                    try { context = JsonSerializer.Deserialize<AssistantChannelContext>(contextJson, JsonSerializerOptions.Web); }
                    catch (JsonException) { return Result.Success(); }
                    if (context is null) return Result.Success();
                    var focused = WhatsAppAssistantAccessPolicy.Focused(context, focus, choice.Spreadsheet);
                    var continuation = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contactId, tap.Id, actor, reference.SubjectUserId,
                        WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision, JsonSerializer.Serialize(focused, JsonSerializerOptions.Web),
                        nowUtc, reference.ContentExpiresAtUtc);
                    if (!continuation.ContinueFrom(reference.RequestId) || !continuation.CarryAttachment(attachment)) return WhatsAppAssistantErrors.StateConflict;
                    requests.Add(continuation);
                    return Result.Success();
                }
                if (choice.Reply is not { } reply) return Result.Success();
                var row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contactId, tap.Id, actor, reference.SubjectUserId,
                    WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision, contextJson, nowUtc, reference.ContentExpiresAtUtc);
                // It answers the document the person sent: never an offer.
                if (!row.CarryAttachment(attachment)) return WhatsAppAssistantErrors.StateConflict;
                // Closing task 9: a reply that applies nothing names its own reference as the proposal (like a spreadsheet's rejection), so it
                // is no turn and does not close the question.
                if (!choice.Applies) reply = reply with { ProposalId = row.RequestId };
                requests.Add(row);
                if (!await automatic.AcceptTerminalAsync(contact, row, reply, token)) row.FinishWithoutReply(true, nowUtc);
                return Result.Success();
            }, CommitPolicy.OnSuccess, ct);
        });
}
