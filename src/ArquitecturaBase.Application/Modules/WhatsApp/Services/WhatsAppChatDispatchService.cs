using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatDispatchService(
    IWhatsAppConversationRepository repository, IWhatsAppMessageRepository messages, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, WhatsAppOutgoingSendGuard sends, ILogger<WhatsAppChatDispatchService> logger) : IWhatsAppChatDispatchService
{
    public Task<Result<WhatsAppDispatchReservationResponse>> ReserveAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppDispatchReservationResponse>>(logger, "ReserveWhatsAppOutgoing", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var outgoing = await repository.GetNextPendingAsync(nowUtc, ct);
            if (outgoing is null) { return Result.Success(new WhatsAppDispatchReservationResponse(false, null)); }
            var contact = await repository.GetForUpdateAsync(outgoing.ContactId, ct);
            var phone = await sends.ValidateAsync(contact, outgoing, ct);
            if (phone.IsFailure)
            {
                outgoing.Fail(phone.Error.Code);
                return Result.Success(new WhatsAppDispatchReservationResponse(true, null));
            }
            Guid? reservation = outgoing.Origin switch
            {
                WhatsAppMessageOrigin.FormAction => outgoing.ReserveFormAction(nowUtc),
                WhatsAppMessageOrigin.Assistant => outgoing.ReserveAssistant(nowUtc),
                _ => null,
            };
            if (reservation is null) outgoing.Start(nowUtc);
            return Result.Success(new WhatsAppDispatchReservationResponse(true, new(outgoing.Id, contact!.Id, phone.Value, outgoing.Body!,
                outgoing.Kind, outgoing.FlowSessionId, outgoing.Origin, reservation, outgoing.Document, outgoing.DocumentMediaId, outgoing.QuickReplies, outgoing.ListButton,
                outgoing.DispatchRetries)));
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> CompleteAsync(Guid contactId, Guid operationId, string? waMessageId, bool uncertain, CancellationToken cancellationToken, Guid? reservationId = null, string? failureCode = null) =>
        OperationLog.RunAsync<Result>(logger, "CompleteWhatsAppOutgoing", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (waMessageId is not null) { await messages.LockAsync([waMessageId], ct); }
            var contact = await repository.GetForUpdateAsync(contactId, ct);
            if (contact is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId) { return Result.Failure(WhatsAppChatErrors.NotFound); }
            if (operation.Origin is WhatsAppMessageOrigin.FormAction or WhatsAppMessageOrigin.Assistant && operation.DispatchReservationId != reservationId)
                return Result.Failure(WhatsAppFormActionErrors.StateConflict);
            if (operation.State == WhatsAppOutgoingMessageState.Accepted) { return Result.Success(); }
            if (waMessageId is not null)
            {
                operation.Accept(waMessageId);
                // Derivar (AS4): «Necesita una persona» goes away when the reply of a person goes out.
                if (operation.Origin == WhatsAppMessageOrigin.Conversation) contact.ClearHandOff();
                if (!(await messages.ListExistingIdsAsync([waMessageId], ct)).Contains(waMessageId))
                {
                    var kind = operation.Kind switch
                    {
                        WhatsAppOutgoingMessageKind.Flow => WhatsAppMessageKind.Flow,
                        WhatsAppOutgoingMessageKind.Document => WhatsAppMessageKind.Document,
                        WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List => WhatsAppMessageKind.Interactive,
                        WhatsAppOutgoingMessageKind.LocationRequest => WhatsAppMessageKind.LocationRequest,
                        _ => WhatsAppMessageKind.Text,
                    };
                    var message = WhatsAppMessage.Outbound(contactId, waMessageId, kind,
                        operation.Body!, timeProvider.GetUtcNow().UtcDateTime);
                    // W1-A: the Flow and the location request keep their send in the chat; «Seguir sin ubicación» only keeps its button.
                    if (operation.FlowSessionId is { } sessionId && operation.Kind is WhatsAppOutgoingMessageKind.Flow or WhatsAppOutgoingMessageKind.LocationRequest)
                        message.AttachFlowSession(sessionId);
                    // W3: the resend keeps its mark in the chat.
                    if (operation.IsResend) message.MarkResend();
                    if (operation.OperatorUserId is { } operatorId) message.AttributeOperator(operatorId);
                    // W1-A: «Seguir sin ubicación» keeps its button in the chat.
                    if (operation.Origin == WhatsAppMessageOrigin.Conversation && operation.Kind == WhatsAppOutgoingMessageKind.QuickReplies
                        && operation.FlowSessionId is not null) message.RecordReplyTitles(operation.QuickReplies.Select(reply => reply.Title).ToArray());
                    if (operation.Origin == WhatsAppMessageOrigin.FormAction) message.AttributeFormAction();
                    if (operation.Origin == WhatsAppMessageOrigin.Assistant) message.AttributeAssistant(
                        operation.Kind is WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List ? operation.QuickReplies.Select(reply => reply.Title).ToArray() : null,
                        operation.AssistantReplyJson, list: operation.Kind == WhatsAppOutgoingMessageKind.List);
                    // VC1-A (task 29): the notice of the loads of a spreadsheet keeps «Por qué lo propuso» in the chat.
                    if (!message.RecordSpreadsheetRows(operation.SpreadsheetRowsJson))
                        throw new InvalidOperationException("The notice of the loads could not keep its rows.");
                    if (await messages.GetReceiptAsync(waMessageId, ct) is { } receipt)
                    {
                        message.ApplyStatus(receipt.Status, receipt.OccurredAtUtc, receipt.ErrorCode);
                    }
                    messages.Add(message);
                }
            }
            else if (operation.State == WhatsAppOutgoingMessageState.Sending)
            {
                if (uncertain) { operation.MarkUncertain(); } else { operation.Fail(failureCode); }
            }

            // H12: this person's six seconds count from here, and meanwhile the next chat is chosen without waiting for them.
            contact.RecordDispatch(timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> RetryLaterAsync(Guid contactId, Guid operationId, Guid? reservationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "RetryWhatsAppOutgoing", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // The contact before its outgoing row, as reserving and completing take them.
            var contact = await repository.GetForUpdateAsync(contactId, ct);
            if (contact is null) return Result.Failure(WhatsAppChatErrors.NotFound);
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId) return Result.Failure(WhatsAppChatErrors.NotFound);
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (!operation.RetryLater(reservationId, nowUtc + WhatsAppDispatchPacing.RetryWait))
                return Result.Failure(WhatsAppFormActionErrors.StateConflict);
            // The turn to write to this person was used, as when a send finishes.
            contact.RecordDispatch(nowUtc);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> ReturnUnsentAsync(Guid contactId, Guid operationId, Guid? reservationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ReturnWhatsAppOutgoing", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // The contact before its outgoing row, as reserving and completing take them.
            if (await repository.GetForUpdateAsync(contactId, ct) is null) return Result.Failure(WhatsAppChatErrors.NotFound);
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId) return Result.Failure(WhatsAppChatErrors.NotFound);
            operation.ReturnUnsent(reservationId);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<bool>> MarkAssistantMessageEffectAsync(Guid contactId, Guid operationId, Guid reservationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<bool>>(logger, "StartWhatsAppAssistantMessage", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var contact = await repository.GetForUpdateAsync(contactId, ct);
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId || !operation.OwnsAssistantReservation(reservationId) || operation.MessageEffectStartedAtUtc.HasValue
                || operation.StartedAtUtc <= timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2)) return Result.Success(false);
            var validation = await sends.ValidateAsync(contact, operation, ct);
            if (validation.IsFailure)
            {
                operation.Fail(validation.Error.Code);
                return Result.Success(false);
            }
            return Result.Success(operation.MarkAssistantMessageEffect(reservationId, timeProvider.GetUtcNow().UtcDateTime));
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<bool>> RecordFormActionMediaAsync(Guid operationId, Guid reservationId, string mediaId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<bool>>(logger, "RecordWhatsAppActionMedia", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            return Result.Success(operation is not null && operation.StartedAtUtc > timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2)
                && operation.RecordFormActionMedia(reservationId, mediaId));
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<bool>> MarkFormActionMessageEffectAsync(Guid contactId, Guid operationId, Guid reservationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<bool>>(logger, "StartWhatsAppActionMessage", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Contact precedes outgoing, matching reservation/completion and number-change lock order.
            var contact = await repository.GetForUpdateAsync(contactId, ct);
            var operation = await repository.GetOutgoingAsync(operationId, ct);
            if (operation is null || operation.ContactId != contactId || !operation.OwnsFormActionReservation(reservationId) || operation.MessageEffectStartedAtUtc.HasValue
                || operation.StartedAtUtc <= timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2)) return Result.Success(false);
            var validation = await sends.ValidateAsync(contact, operation, ct);
            if (validation.IsFailure)
            {
                operation.Fail(validation.Error.Code);
                return Result.Success(false);
            }
            return Result.Success(operation.MarkFormActionMessageEffect(reservationId, timeProvider.GetUtcNow().UtcDateTime));
        }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> RecoverExpiredAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "RecoverWhatsAppOutgoing", () =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var operation in await repository.ListExpiredSendingAsync(timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-2), ct))
            {
                if (operation.Origin == WhatsAppMessageOrigin.FormAction) operation.RecoverExpiredFormAction();
                else if (operation.Origin == WhatsAppMessageOrigin.Assistant) operation.RecoverExpiredAssistant();
                else operation.MarkUncertain();
            }

            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));

}
