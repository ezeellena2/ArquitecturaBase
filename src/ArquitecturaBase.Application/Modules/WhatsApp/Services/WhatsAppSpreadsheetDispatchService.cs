using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Stage 2 of the assistant authoring: the inbound processor gets a received spreadsheet downloaded, prepared and copied, as an audio.
// Who may have it downloaded is decided before any I/O; Meta, the owner's reader and the private copy stay outside the transaction,
// which locks the contact and then the message and keeps what came of it once. A failure that may pass waits with the usual scale,
// and so does a fault of any of the three, so the spreadsheet is not downloaded again in every round.
internal sealed partial class WhatsAppSpreadsheetDispatchService(IWhatsAppSpreadsheetReader reader, IWhatsAppSpreadsheetRepository messages,
    IWhatsAppConversationRepository conversations, WhatsAppSpreadsheetPreparationPolicy preparation, WhatsAppSpreadsheetReplyPolicy replies,
    IUnitOfWork unitOfWork, TimeProvider clock, ILogger<WhatsAppSpreadsheetDispatchService> logger) : IWhatsAppSpreadsheetDispatchService
{
    public Task<Result> PrepareAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "PrepareWhatsAppSpreadsheet", () => PrepareCoreAsync(contactId, cancellationToken));

    private async Task<Result> PrepareCoreAsync(Guid contactId, CancellationToken ct)
    {
        var source = await reader.FindPendingAsync(contactId, clock.GetUtcNow().UtcDateTime, ct);
        if (source is null) return Result.Success();
        var access = await preparation.AccessAsync(source, ct);
        var acquired = access.Downloads ? await AcquireAsync(source, access, ct) : null;
        // VC1-A: one that cannot be read, of someone who loads, enters the inbox discarded before it is answered.
        if (access.Load && acquired is { IsSuccess: true, Value.Rejection: { } rejected } && rejected != StructuredAttachmentRejection.Unavailable)
            await preparation.RegisterRejectedAsync(source, rejected, ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await conversations.GetForUpdateAsync(contactId, token) is not { } contact) return Result.Success();
            var message = await messages.GetForUpdateAsync(contactId, source.MessageId, token);
            var current = await reader.FindAsync(contactId, source.MessageId, token);
            if (message is null || current is not { State: WhatsAppSpreadsheetState.Pending }) return Result.Success();
            // Losing the link, the mode or a permission while downloading, or relinking, leaves it as if it had never qualified: the access is
            // compared whole, as documents do, so a copy prepared to load its rows is not kept for one who stopped loading.
            var now = current.LinkRevision != source.LinkRevision || current.Mode != source.Mode ? null : await preparation.AccessAsync(current, token);
            if (acquired is null && access.NotAllowed && now == access && current.LinkedUserId is { } subject)
            {
                // DOC4 (rule B8): told once and handed to a person, as a document; it is neither downloaded nor registered.
                await replies.RefuseAsync(contact, message, subject, access.Culture!, token);
                return Result.Success();
            }
            if (acquired is null || now != access)
            {
                message.SetSpreadsheetUnavailable(WhatsAppAssistantErrors.Unavailable.Code);
                return Result.Success();
            }
            if (acquired.IsFailure)
            {
                if (acquired.Error == FileErrors.Unavailable) message.RegisterSpreadsheetFailure(clock.GetUtcNow().UtcDateTime, acquired.Error.Code);
                else message.SetSpreadsheetUnavailable(acquired.Error.Code);
                return Result.Success();
            }
            if (acquired.Value.Rejection is { } rejection)
            {
                if (rejection == StructuredAttachmentRejection.Unavailable) message.SetSpreadsheetUnavailable(WhatsAppSpreadsheetReplyPolicy.ReasonCode(rejection));
                else await replies.RejectAsync(contact, message, rejection, token);
                return Result.Success();
            }
            var stored = message.StoreSpreadsheet(acquired.Value.StorageKey!, acquired.Value.Size, acquired.Value.Sha256!,
                JsonSerializer.Serialize(acquired.Value.Spreadsheet, JsonSerializerOptions.Web));
            if (stored.IsFailure) message.SetSpreadsheetUnavailable(stored.Error.Code);
            return Result.Success();
        }, CommitPolicy.OnSuccess, ct);
    }

    // A fault of Meta's download, the owner's reader or the private storage is a failure that may pass, as an unavailable download.
    private async Task<Result<WhatsAppPreparedSpreadsheet>> AcquireAsync(WhatsAppSpreadsheetSourceRow source, WhatsAppSpreadsheetAccess access, CancellationToken ct)
    {
        try { return await preparation.AcquireAsync(source, access, ct); }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogAcquisitionFailed(logger, exception);
            return FileErrors.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A received spreadsheet could not be downloaded, read or copied; it is retried with the usual scale")]
    private static partial void LogAcquisitionFailed(ILogger logger, Exception exception);
}
