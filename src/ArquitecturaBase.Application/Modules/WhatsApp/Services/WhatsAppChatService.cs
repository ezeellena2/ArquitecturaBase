using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatService(
    IWhatsAppChatReader reader, IWhatsAppConversationRepository repository, IRequestValidator validator,
    ICurrentUser currentUser, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    IPhoneNumberParser phoneNumbers, ILogger<WhatsAppChatService> logger) : IWhatsAppChatService
{
    private Guid Actor => currentUser.UserId ?? throw new InvalidOperationException("The chat service requires an authenticated operator.");

    public Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppChatsResponse>>(logger, "ListWhatsAppChats", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            var page = await reader.ListChatsAsync(request, Actor, cancellationToken);
            var counts = await reader.CountChatsAsync(request, Actor, cancellationToken);
            return new WhatsAppChatsResponse(new PagedResult<WhatsAppChatResponse>(
                [.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.TotalCount), counts.AllCount, counts.UnreadCount);
        });

    public Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppThreadResponse>>(logger, "GetWhatsAppThread", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            var row = await reader.FindThreadAsync(request, Actor, cancellationToken);
            return row is null ? WhatsAppChatErrors.NotFound : new WhatsAppThreadResponse(ToResponse(row.Contact),
                [.. row.Messages.Select(message => new WhatsAppMessageResponse(message.Id, message.Direction, message.Kind,
                    message.Purpose == WhatsAppMessagePurpose.Access ? null : message.Body, message.OccurredAtUtc, message.InboundSequence, message.OperatorUserId, message.Status, message.ErrorCode, message.Purpose, message.AccessEventKind))],
                [.. row.Outgoing.Select(ToResponse)], row.BeforeAtUtc, row.BeforeId);
        });

    public async Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId, CancellationToken cancellationToken)
    {
        var row = await reader.FindOutgoingAsync(contactId, requestId, Actor, cancellationToken);
        return row is null ? WhatsAppChatErrors.NotFound : ToResponse(row);
    }

    public Task<Result<WhatsAppOutgoingMessageResponse>> SendTextAsync(SendWhatsAppTextRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger, "SendWhatsAppText", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var contact = await repository.GetForUpdateAsync(request.ContactId, ct);
                if (contact is null) { return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.NotFound); }
                var body = request.Body!.Trim();
                var existing = await repository.GetByRequestAsync(contact.Id, Actor, request.RequestId, ct);
                if (existing is not null)
                {
                    return string.Equals(existing.Body, body, StringComparison.Ordinal)
                        ? Result.Success(ToResponse(existing))
                        : Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.RequestConflict);
                }

                if (contact.Mode != WhatsAppConversationMode.Manual) { return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.ManualRequired); }
                if (!IsReplyWindowOpen(contact, timeProvider.GetUtcNow().UtcDateTime)) { return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.WindowClosed); }
                if (WhatsAppIds.Parse(phoneNumbers, contact.WaId).IsFailure) { return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.DestinationMissing); }
                if (await repository.ExistsUnresolvedOutgoingAsync(contact.Id, ct)) { return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.SendPending); }
                var outgoing = WhatsAppOutgoingMessage.Create(Actor, contact.Id, request.RequestId, body, timeProvider.GetUtcNow().UtcDateTime);
                repository.Add(outgoing);
                return Result.Success(ToResponse(outgoing));
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result> ChangeModeAsync(ChangeWhatsAppModeRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ChangeWhatsAppMode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var contact = await repository.GetForUpdateAsync(request.ContactId, ct);
                if (contact is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
                if (contact.ModeRevision != request.ExpectedRevision) { return Result.Failure(WhatsAppChatErrors.RevisionConflict); }
                if (await repository.ExistsUnresolvedOutgoingAsync(contact.Id, ct)) { return Result.Failure(WhatsAppChatErrors.SendPending); }
                contact.ChangeMode(request.Mode);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result> MarkReadAsync(MarkWhatsAppChatReadRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "MarkWhatsAppChatRead", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var contact = await repository.GetForUpdateAsync(request.ContactId, ct);
                if (contact is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
                if (request.ThroughSequence > contact.LastInboundSequence) { return Result.Failure(WhatsAppChatErrors.ReadPositionInvalid); }
                var state = await repository.GetReadStateAsync(contact.Id, Actor, ct);
                if (state is null)
                {
                    state = WhatsAppReadState.Create(Actor, contact.Id);
                    repository.Add(state);
                }

                state.Advance(request.ThroughSequence);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result> ReviewUncertainAsync(Guid contactId, Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ReviewWhatsAppSend", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (await repository.GetForUpdateAsync(contactId, ct) is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
                var operation = await repository.GetByRequestAsync(contactId, Actor, requestId, ct);
                if (operation is null) { return Result.Failure(WhatsAppChatErrors.NotFound); }
                if (operation.State == WhatsAppOutgoingMessageState.Reviewed) { return Result.Success(); }
                if (operation.State != WhatsAppOutgoingMessageState.Uncertain) { return Result.Failure(WhatsAppChatErrors.SendPending); }
                operation.Review();
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken));

    internal static bool IsReplyWindowOpen(WhatsAppContact contact, DateTime nowUtc) =>
        contact.LastInboundAtUtc > nowUtc.AddHours(-24) && contact.LastInboundAtUtc <= nowUtc.AddMinutes(5);

    private WhatsAppChatResponse ToResponse(WhatsAppChatRow row)
    {
        var parsed = WhatsAppIds.Parse(phoneNumbers, row.PhoneNumber);
        return new(row.Id, row.ProfileName, parsed.IsSuccess ? phoneNumbers.FormatInternational(parsed.Value) : null,
            row.Mode, row.ModeRevision, row.LastInboundAtUtc, row.LastInboundSequence, row.UnreadCount, row.PreviewPurpose == WhatsAppMessagePurpose.Access ? null : row.Preview, row.PreviewPurpose, row.PreviewAccessEventKind);
    }

    private static WhatsAppOutgoingMessageResponse ToResponse(WhatsAppOutgoingMessageRow row) =>
        new(row.Id, row.RequestId, row.OperatorUserId, row.Body, row.RequestedAtUtc, row.State, row.WaMessageId);

    internal static WhatsAppOutgoingMessageResponse ToResponse(WhatsAppOutgoingMessage operation) =>
        new(operation.Id, operation.RequestId, operation.OperatorUserId, operation.Body, operation.RequestedAtUtc, operation.State, operation.WaMessageId);
}
