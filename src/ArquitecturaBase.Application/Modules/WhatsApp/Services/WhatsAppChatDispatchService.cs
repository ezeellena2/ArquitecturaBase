using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatDispatchService(
    IWhatsAppConversationRepository repository, IWhatsAppMessageRepository messages, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, IPhoneNumberParser phoneNumbers, IPermissionService permissions, IUserReader users) : IWhatsAppChatDispatchService
{
    public Task<Result<WhatsAppDispatchResponse?>> ReserveAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var outgoing = await repository.GetNextPendingAsync(ct);
            if (outgoing is null) { return Result.Success<WhatsAppDispatchResponse?>(null); }
            var contact = await repository.GetForUpdateAsync(outgoing.ContactId, ct);
            var actor = await users.FindByIdAsync(outgoing.OperatorUserId, ct);
            var allowed = await permissions.GetPermissionsAsync(outgoing.OperatorUserId, ct);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var phone = WhatsAppIds.Parse(phoneNumbers, contact?.WaId);
            if (contact is null || contact.Mode != WhatsAppConversationMode.Manual || !WhatsAppChatService.IsReplyWindowOpen(contact, nowUtc)
                || actor is not { IsActive: true } || !allowed.Contains(WhatsAppPermissions.Read) || !allowed.Contains(WhatsAppPermissions.Send)
                || phone.IsFailure || string.IsNullOrWhiteSpace(outgoing.Body))
            {
                outgoing.Fail();
                return Result.Success<WhatsAppDispatchResponse?>(null);
            }

            outgoing.Start(nowUtc);
            return Result.Success<WhatsAppDispatchResponse?>(new(outgoing.Id, contact.Id, phone.Value, outgoing.Body,
                outgoing.Kind, outgoing.FlowSessionId));
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> CompleteAsync(Guid contactId, Guid operationId, string? waMessageId, bool uncertain, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (waMessageId is not null) { await messages.LockAsync([waMessageId], ct); }
            if (await repository.GetForUpdateAsync(contactId, ct) is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId) { return Result.Failure(WhatsAppChatErrors.NotFound); }
            if (operation.State == WhatsAppOutgoingMessageState.Accepted) { return Result.Success(); }
            if (waMessageId is not null)
            {
                operation.Accept(waMessageId);
                if (!(await messages.ListExistingIdsAsync([waMessageId], ct)).Contains(waMessageId))
                {
                    var kind = operation.Kind == WhatsAppOutgoingMessageKind.Flow ? WhatsAppMessageKind.Flow : WhatsAppMessageKind.Text;
                    var message = WhatsAppMessage.Outbound(contactId, waMessageId, kind,
                        operation.Body!, timeProvider.GetUtcNow().UtcDateTime);
                    if (operation.FlowSessionId is { } sessionId) message.AttachFlowSession(sessionId);
                    message.AttributeOperator(operation.OperatorUserId);
                    if (await messages.GetReceiptAsync(waMessageId, ct) is { } receipt)
                    {
                        message.ApplyStatus(receipt.Status, receipt.OccurredAtUtc, receipt.ErrorCode);
                    }
                    messages.Add(message);
                }
            }
            else if (operation.State == WhatsAppOutgoingMessageState.Sending)
            {
                if (uncertain) { operation.MarkUncertain(); } else { operation.Fail(); }
            }

            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

    public Task<Result> RecoverExpiredAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var operation in await repository.ListExpiredSendingAsync(timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2), ct))
            {
                operation.MarkUncertain();
            }

            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken);

}
