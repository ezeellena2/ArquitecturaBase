using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Task 8 of the documents: the inbound processor gets a received document decided, downloaded, prepared and copied, as a spreadsheet, and
// read by the engine only once it will be used, polled like an audio's normalization. Meta, the owner's reader, the private copy, the
// inbox's port and the engine stay outside the transaction, which locks the contact and then the message, checks the decision again with
// the same link and keeps what came of it once. A failure that may pass waits with the usual scale, and so does a fault of any of them.
internal sealed partial class WhatsAppReceivedDocumentDispatchService(IWhatsAppReceivedDocumentReader reader, IWhatsAppReceivedDocumentRepository messages,
    IWhatsAppConversationRepository conversations, WhatsAppReceivedDocumentPreparationPolicy preparation, WhatsAppReceivedDocumentReplyPolicy replies,
    WhatsAppReceivedDocumentReadingPolicy reading, IUnitOfWork unitOfWork, ILogger<WhatsAppReceivedDocumentDispatchService> logger)
    : IWhatsAppReceivedDocumentDispatchService
{
    private static readonly Result<WhatsAppAssistantWorkResponse?> Nothing = Result.Success<WhatsAppAssistantWorkResponse?>(null);

    // A pending document is decided, and downloaded when it qualifies, before the transaction; a reading in course is only polled in it.
    // The reader takes one that waits for its request only once its time passed or another message followed it.
    public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId, CancellationToken ct) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantWorkResponse?>>(logger, "PrepareWhatsAppReceivedDocument", async () =>
        {
            var nowUtc = reading.NowUtc;
            var source = await reader.FindPendingAsync(contactId, nowUtc, ct);
            if (source is null) return Nothing;
            var plan = source.State == WhatsAppDocumentState.Pending ? await preparation.DecideAsync(source, nowUtc, ct) : null;
            var acquired = plan?.Decision == WhatsAppReceivedDocumentDecision.Download ? await AcquireAsync(source, plan, ct) : null;
            return await unitOfWork.ExecuteInTransactionAsync(token => plan is null ? PollAsync(source, token) : KeepAsync(source, plan, acquired, nowUtc, token),
                CommitPolicy.OnSuccess, ct);
        });

    private async Task<Result<WhatsAppAssistantWorkResponse?>> KeepAsync(WhatsAppReceivedDocumentSourceRow source, WhatsAppReceivedDocumentPlan plan,
        Result<WhatsAppPreparedDocument>? acquired, DateTime nowUtc, CancellationToken token)
    {
        if (await conversations.GetForUpdateAsync(source.ContactId, token) is not { } contact) return Nothing;
        var message = await messages.GetForUpdateAsync(source.ContactId, source.MessageId, token);
        var current = await reader.FindAsync(source.ContactId, source.MessageId, token);
        if (message is null || current is not { State: WhatsAppDocumentState.Pending }) return Nothing;
        // Losing the link or the mode while downloading, or relinking, leaves it as if it never qualified. Otherwise what is decided here
        // applies, so a request that came in between is not lost: only a download taken with this same decision is kept, and one that now
        // needs a download, or another one, stays pending for the next round.
        var decided = current.LinkRevision == source.LinkRevision && current.Mode == source.Mode ? await preparation.DecideAsync(current, nowUtc, token)
            : WhatsAppReceivedDocumentPlan.Keep;
        switch (decided.Decision)
        {
            case WhatsAppReceivedDocumentDecision.Keep:
                message.SetDocumentUnavailable(WhatsAppAssistantErrors.Unavailable.Code);
                return Nothing;
            case WhatsAppReceivedDocumentDecision.NotLinked:
                message.SetDocumentUnavailable(WhatsAppReceivedDocumentPreparationPolicy.NotLinkedCode);
                return Nothing;
            case WhatsAppReceivedDocumentDecision.NotAllowed:
                await replies.RefuseAsync(contact, message, current.LinkedUserId!.Value, decided.Culture!, token);
                return Nothing;
            case WhatsAppReceivedDocumentDecision.Wait:
                message.AwaitDocumentRequest(decided.AwaitedUntilUtc!.Value);
                return Nothing;
        }
        if (decided != plan || acquired is null) return Nothing;
        if (acquired.IsFailure)
        {
            if (acquired.Error == FileErrors.Unavailable) message.RegisterDocumentFailure(reading.NowUtc, acquired.Error.Code);
            else message.SetDocumentUnavailable(acquired.Error.Code);
            return Nothing;
        }
        if (acquired.Value.Rejection is { } rejection)
        {
            if (rejection == StructuredAttachmentRejection.Unavailable) message.SetDocumentUnavailable(WhatsAppReceivedDocumentReplyPolicy.ReasonCode(rejection));
            else await replies.RejectAsync(contact, message, rejection, acquired.Value.FileKind, token);
            return Nothing;
        }
        return await StoreAsync(contact, message, current, plan, acquired.Value, token);
    }

    // The copy and what its owner prepared, once. A document that will not be used stays in the chat without spending a reading; a Word
    // is read with its own reading; a PDF or a photo asks the engine, with the person's context captured under the lock.
    private async Task<Result<WhatsAppAssistantWorkResponse?>> StoreAsync(WhatsAppContact contact, WhatsAppMessage message,
        WhatsAppReceivedDocumentSourceRow current, WhatsAppReceivedDocumentPlan plan, WhatsAppPreparedDocument acquired, CancellationToken ct)
    {
        var document = acquired.Document!;
        var stored = message.StoreDocument(acquired.StorageKey!, acquired.Size, acquired.Sha256!, document.Pages,
            document.Reading is { } prepared ? WhatsAppReceivedDocumentReadingPolicy.SerializeReading(prepared) : null);
        if (stored.IsFailure)
        {
            message.SetDocumentUnavailable(stored.Error.Code);
            return Nothing;
        }
        if (!plan.Reads) return Nothing;
        if (document.Reading is not null) return message.UsePreparedDocumentReading().IsSuccess ? Nothing : Result.Failure<WhatsAppAssistantWorkResponse?>(WhatsAppChatErrors.RequestConflict);
        // A PDF or a photo declared as something else stays stored where the installation does not read documents.
        if (!plan.EngineReads) return Nothing;
        // Without the person's context it stays in the chat, stored, as a document that is not used.
        var context = await reading.CaptureAsync(current, ct);
        if (context.IsFailure) return Nothing;
        var expiresAtUtc = reading.ExpiresAtUtc(current);
        var request = message.RequestDocumentReading(context.Value.ActorUserId, System.Text.Json.JsonSerializer.Serialize(context.Value, System.Text.Json.JsonSerializerOptions.Web),
            contact.LinkRevision, expiresAtUtc, reading.NowUtc);
        if (request.IsFailure) return Result.Failure<WhatsAppAssistantWorkResponse?>(request.Error);
        var operation = WhatsAppReceivedDocumentReadingPolicy.Operation(request.Value, context.Value, message.DocumentStorageKey!, message.DocumentFileName!,
            message.DocumentSize!.Value, message.DocumentCopySha256!, message.DocumentPages, message.OccurredAtUtc, expiresAtUtc);
        return operation is null ? Result.Failure<WhatsAppAssistantWorkResponse?>(WhatsAppChatErrors.RequestConflict)
            : Result.Success<WhatsAppAssistantWorkResponse?>(new(request.Value, operation));
    }

    // A reading in course is polled with the context sealed with its request, while its content lasts and the person still may.
    private async Task<Result<WhatsAppAssistantWorkResponse?>> PollAsync(WhatsAppReceivedDocumentSourceRow source, CancellationToken token)
    {
        if (await conversations.GetForUpdateAsync(source.ContactId, token) is null) return Nothing;
        var message = await messages.GetForUpdateAsync(source.ContactId, source.MessageId, token);
        var current = await reader.FindAsync(source.ContactId, source.MessageId, token);
        if (message is null || current is not { State: WhatsAppDocumentState.Reading, RequestId: { } requestId }) return Nothing;
        if (reading.IsExpired(current))
        {
            message.SetDocumentReadingState(WhatsAppDocumentState.Expired);
            return Nothing;
        }
        var context = await reading.RevalidateAsync(current, token);
        if (context.IsFailure)
        {
            message.SetDocumentReadingState(WhatsAppDocumentState.Unavailable, context.Error.Code);
            return Nothing;
        }
        var operation = current is { StorageKey: { } key, FileName: { } name, Size: { } size, CopySha256: { } sha256 }
            ? WhatsAppReceivedDocumentReadingPolicy.Operation(requestId, context.Value, key, name, size, sha256, current.Pages, current.OccurredAtUtc,
                current.ContentExpiresAtUtc ?? reading.ExpiresAtUtc(current))
            : null;
        if (operation is null)
        {
            message.SetDocumentReadingState(WhatsAppDocumentState.Failed, FileErrors.InvalidMetadata.Code);
            return Nothing;
        }
        return Result.Success<WhatsAppAssistantWorkResponse?>(new(requestId, operation));
    }

    public Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "CompleteWhatsAppReceivedDocument", async () =>
        {
            // Over the day's reading quota the document goes to the inbox unclassified before its state is final, outside the transaction;
            // registering it again answers the same.
            if (WhatsAppReceivedDocumentReadingPolicy.IsQuota(outcome) && await reader.FindByRequestAsync(contactId, requestId, ct) is { State: WhatsAppDocumentState.Reading } quoted
                && await reading.RevalidateAsync(quoted, ct) is { IsSuccess: true } sealedContext)
                await preparation.RegisterOverQuotaAsync(quoted, sealedContext.Value.Culture, ct);
            return await unitOfWork.ExecuteInTransactionAsync(token => ApplyAsync(contactId, requestId, outcome, token), CommitPolicy.OnSuccess, ct);
        });

    private async Task<Result> ApplyAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct)
    {
        if (await conversations.GetForUpdateAsync(contactId, ct) is not { } contact) return Result.Success();
        var message = await messages.GetByRequestForUpdateAsync(contactId, requestId, ct);
        var source = message is null ? null : await reader.FindAsync(contactId, message.Id, ct);
        if (message is null || source is not { State: WhatsAppDocumentState.Reading }) return Result.Success();
        if (reading.IsExpired(source))
        {
            message.SetDocumentReadingState(WhatsAppDocumentState.Expired);
            return Result.Success();
        }
        var authorized = await reading.RevalidateAsync(source, ct);
        if (authorized.IsFailure)
        {
            message.SetDocumentReadingState(WhatsAppDocumentState.Unavailable, authorized.Error.Code);
            return Result.Success();
        }
        if (outcome.IsFailure)
        {
            if (WhatsAppReceivedDocumentReadingPolicy.IsRetryable(outcome.Error)) message.SetDocumentReadingState(WhatsAppDocumentState.Reading);
            else if (WhatsAppReceivedDocumentReadingPolicy.IsQuota(outcome))
            {
                // Task 9: the day's quota is said once, with its fixed text (the inbox registered it unclassified before).
                message.SetDocumentReadingState(WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.QuotaCode);
                await replies.QuotaAsync(contact, message, ct);
            }
            else message.SetDocumentReadingState(WhatsAppDocumentState.Unavailable, outcome.Error.Code);
            return Result.Success();
        }
        if (outcome.Value.RequestId != requestId) return Result.Failure(WhatsAppChatErrors.RequestConflict);
        switch (outcome.Value.State)
        {
            case "completed":
                if (outcome.Value.DocumentReading is not { } read)
                {
                    message.SetDocumentReadingState(WhatsAppDocumentState.Failed);
                    return Result.Success();
                }
                return message.CompleteDocumentReading(requestId, WhatsAppReceivedDocumentReadingPolicy.SerializeReading(read), reading.NowUtc);
            case "pending" or "running" or "queued":
                message.SetDocumentReadingState(WhatsAppDocumentState.Reading);
                break;
            case "uncertain":
                message.SetDocumentReadingState(WhatsAppDocumentState.Uncertain, outcome.Value.FailureCode);
                break;
            case "expired":
                message.SetDocumentReadingState(WhatsAppDocumentState.Expired);
                break;
            default:
                message.SetDocumentReadingState(WhatsAppDocumentState.Failed, outcome.Value.FailureCode);
                break;
        }
        return Result.Success();
    }

    // A fault of Meta's download, the owner's reader, the private storage or the inbox is a failure that may pass, as an unavailable download.
    private async Task<Result<WhatsAppPreparedDocument>> AcquireAsync(WhatsAppReceivedDocumentSourceRow source, WhatsAppReceivedDocumentPlan plan, CancellationToken ct)
    {
        try { return await preparation.AcquireAsync(source, plan, ct); }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogAcquisitionFailed(logger, exception);
            return FileErrors.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A received document could not be downloaded, read or copied; it is retried with the usual scale")]
    private static partial void LogAcquisitionFailed(ILogger logger, Exception exception);
}
