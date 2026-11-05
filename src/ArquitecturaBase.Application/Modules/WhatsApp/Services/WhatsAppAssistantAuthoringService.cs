using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// IA3-A: a tap gets its own reference, answered like an acknowledgement in the same outbox, and is marked in the same commit,
// so a retry after the commit finds it answered. The reply to «Cambiar algo» names the operation the next message changes.
internal sealed class WhatsAppAssistantAuthoringService(IWhatsAppAssistantReader reader, IWhatsAppAssistantRepository requests,
    IWhatsAppConversationRepository conversations, IUnitOfWork unitOfWork, WhatsAppAssistantAutomaticPolicy automatic,
    WhatsAppAssistantAuthoringPolicy authoring, TimeProvider clock, ILogger<WhatsAppAssistantAuthoringService> logger) : IWhatsAppAssistantAuthoringService
{
    public Task<Result> ProcessTapAsync(Guid contactId, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppAuthoringTap", async () =>
        {
            var tap = await reader.FindLatestAuthoringTapAsync(contactId, ct);
            if (tap is null) return Result.Success();
            var reply = await authoring.ReplyAsync(contactId, tap, ct);
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var contact = await conversations.GetForUpdateAsync(contactId, token);
                var current = await reader.FindLatestAuthoringTapAsync(contactId, token);
                if (contact is null || current?.Id != tap.Id || current.ReplyId != tap.ReplyId) return Result.Success();
                await automatic.MarkChoiceAsync(tap, contactId, token);
                if (reply is not { Reference: { ActorUserId: {} actor, ContextJson: {} contextJson, ProposalId: {} proposalId } reference }
                    || contact.Mode != WhatsAppConversationMode.Assistant || contact.WaId is null || contact.UserId != reference.SubjectUserId
                    || contact.LinkRevision != reference.LinkRevision
                    || await requests.GetBySourceAsync(contactId, tap.Id, WhatsAppAssistantRequestKind.Automatic, token) is not null)
                    return Result.Success();
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                var row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contactId, tap.Id, actor, reference.SubjectUserId,
                    WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision, contextJson, nowUtc, nowUtc.AddDays(1));
                if (reply.ChangeRequestId is {} change && !row.AwaitChange(change)) return WhatsAppAssistantErrors.StateConflict;
                requests.Add(row);
                return automatic.AcceptTerminal(contact, row, new WhatsAppAssistantTerminalResponse(proposalId, reply.Body, reply.Culture))
                    ? Result.Success() : WhatsAppAssistantErrors.StateConflict;
            }, CommitPolicy.OnSuccess, ct);
        });
}
