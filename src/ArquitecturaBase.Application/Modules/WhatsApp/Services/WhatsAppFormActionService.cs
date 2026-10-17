using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormActionService(IWhatsAppFormActionRepository repository, IUnitOfWork unitOfWork,
    WhatsAppFormActionPolicy policy, TimeProvider timeProvider, ILogger<WhatsAppFormActionService> logger) : IWhatsAppFormActionService
{
    public async Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "EnqueueWhatsAppFormAction");
        if (request.RunId == Guid.Empty || request.RecipientUserId == Guid.Empty || !WhatsAppOutgoingMessage.IsVisibleBody(request.Body)
            || request.Body.Length > (request.Document is null ? WhatsAppMessage.MaxBodyLength : 1024))
            return Failed(WhatsAppFormActionErrors.InvalidMessage, false);
        WhatsAppOutgoingDocument? document = request.Document is { } item ? new(item.StorageKey, item.Name, item.ContentType, item.Size, item.Sha256) : null;
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockAsync(request.RunId, request.RecipientUserId, ct);
            if (await repository.GetByRunAsync(request.RunId, request.RecipientUserId, ct) is { } existing)
                return Result.Success(existing.Body == request.Body && existing.Document == document ? ToStatus(existing) : Failed(WhatsAppFormActionErrors.StateConflict, false));
            var contact = await repository.GetContactForUpdateAsync(request.RecipientUserId, ct);
            if (await policy.ValidateAsync(contact, request.RecipientUserId, null, ct) is { } error) return Result.Success(Failed(error, true));
            var outgoing = WhatsAppOutgoingMessage.CreateFormAction(request.RecipientUserId, contact!.Id, request.RunId, request.Body,
                timeProvider.GetUtcNow().UtcDateTime, document, contact.LinkRevision, contact.WaId);
            repository.Add(outgoing);
            return Result.Success(ToStatus(outgoing));
        }, CommitPolicy.OnSuccess, cancellationToken);
        OperationLog.Handled(logger, "EnqueueWhatsAppFormAction");
        return result.Value;
    }

    public async Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "GetWhatsAppFormAction");
        // No provider lookup exists without a confirmed message id. Absence of a receipt is not evidence of failure.
        var outgoing = await repository.GetByRunAsync(runId, recipientUserId, cancellationToken);
        OperationLog.Handled(logger, "GetWhatsAppFormAction");
        return outgoing is null ? new(StructuredRecordActionState.NotFound) : ToStatus(outgoing);
    }

    public async Task<StructuredRecordActionResponse> ReviewAsync(Guid runId, Guid recipientUserId, StructuredRecordActionReview review, CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "ReviewWhatsAppFormAction");
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockAsync(runId, recipientUserId, ct);
            var outgoing = await repository.GetByRunForUpdateAsync(runId, recipientUserId, ct);
            if (outgoing is null) return Result.Success(new StructuredRecordActionResponse(StructuredRecordActionState.NotFound));
            outgoing.ReviewFormAction(review.ExpectedRevision, review.WasSent, review.ReviewedByUserId, review.ReviewedAtUtc);
            return Result.Success(ToStatus(outgoing));
        }, CommitPolicy.OnSuccess, cancellationToken);
        OperationLog.Handled(logger, "ReviewWhatsAppFormAction");
        return result.Value;
    }

    public async Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, long controlRevision, CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "RetryWhatsAppFormAction");
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockAsync(runId, recipientUserId, ct);
            var outgoing = await repository.GetByRunAsync(runId, recipientUserId, ct);
            if (outgoing is null) return Result.Success(new StructuredRecordActionResponse(StructuredRecordActionState.NotFound));
            if (outgoing.State != WhatsAppOutgoingMessageState.Failed) return Result.Success(ToStatus(outgoing));
            if (string.IsNullOrWhiteSpace(outgoing.Body)) return Result.Success(Failed(WhatsAppFormActionErrors.InvalidMessage, false));
            var contact = await repository.GetContactForUpdateAsync(recipientUserId, ct);
            outgoing = await repository.GetByRunForUpdateAsync(runId, recipientUserId, ct);
            if (outgoing is null) return Result.Success(new StructuredRecordActionResponse(StructuredRecordActionState.NotFound));
            if (outgoing.State != WhatsAppOutgoingMessageState.Failed) return Result.Success(ToStatus(outgoing));
            if (string.IsNullOrWhiteSpace(outgoing.Body)) return Result.Success(Failed(WhatsAppFormActionErrors.InvalidMessage, false));
            if (await policy.ValidateAsync(contact, recipientUserId, outgoing, ct) is { } error)
                return Result.Success(Failed(error, error != WhatsAppFormActionErrors.RecipientChanged));
            outgoing.RetryFormAction(controlRevision, timeProvider.GetUtcNow().UtcDateTime);
            return Result.Success(ToStatus(outgoing));
        }, CommitPolicy.OnSuccess, cancellationToken);
        OperationLog.Handled(logger, "RetryWhatsAppFormAction");
        return result.Value;
    }

    private static StructuredRecordActionResponse Failed(Error error, bool canRetry) => new(StructuredRecordActionState.Failed, error.Code, canRetry);
    private static StructuredRecordActionResponse ToStatus(WhatsAppOutgoingMessage outgoing) => new(outgoing.State switch
    {
        WhatsAppOutgoingMessageState.Pending => StructuredRecordActionState.Pending,
        WhatsAppOutgoingMessageState.Sending => StructuredRecordActionState.Sending,
        WhatsAppOutgoingMessageState.Accepted => StructuredRecordActionState.Accepted,
        WhatsAppOutgoingMessageState.Reviewed when outgoing.DeliveryReviewWasSent == true => StructuredRecordActionState.Accepted,
        WhatsAppOutgoingMessageState.Failed => StructuredRecordActionState.Failed,
        _ => StructuredRecordActionState.Uncertain,
    }, outgoing.FailureCode, outgoing.State == WhatsAppOutgoingMessageState.Failed && !string.IsNullOrWhiteSpace(outgoing.Body)
        && outgoing.FailureCode != WhatsAppFormActionErrors.RecipientChanged.Code, Review: outgoing.DeliveryReviewWasSent is {} wasSent
            ? new StructuredRecordActionReview(outgoing.LastFormActionControlRevision, wasSent, outgoing.DeliveryReviewedByUserId!.Value, outgoing.DeliveryReviewedAtUtc!.Value) : null);
}
