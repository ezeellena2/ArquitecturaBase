using ArquitecturaBase.Application.Common.Logging;
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
    IWhatsAppChatQueryService queries, IWhatsAppConversationRepository repository, IRequestValidator validator,
    ICurrentUser currentUser, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    IPhoneNumberParser phoneNumbers, ILogger<WhatsAppChatService> logger) : IWhatsAppChatService
{
    private Guid Actor => currentUser.UserId ?? throw new InvalidOperationException("The chat service requires an authenticated operator.");

    public Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppChatsResponse>>(logger, "ListWhatsAppChats", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            return await queries.ListAsync(request, Actor, cancellationToken);
        });

    public Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppThreadResponse>>(logger, "GetWhatsAppThread", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) { return error; }
            return await queries.GetThreadAsync(request, Actor, cancellationToken);
        });

    public async Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId, CancellationToken cancellationToken)
    {
        return await queries.FindOutgoingAsync(contactId, requestId, Actor, cancellationToken);
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

    internal static WhatsAppOutgoingMessageResponse ToResponse(WhatsAppOutgoingMessageRow row) =>
        new(row.Id, row.RequestId, row.OperatorUserId, row.Body, row.RequestedAtUtc, row.State, row.WaMessageId);

    internal static WhatsAppOutgoingMessageResponse ToResponse(WhatsAppOutgoingMessage operation) =>
        new(operation.Id, operation.RequestId, operation.OperatorUserId, operation.Body, operation.RequestedAtUtc, operation.State, operation.WaMessageId, Kind: operation.Kind);
}
