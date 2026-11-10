using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// Stage 2 of the assistant authoring: the inbound processor gets a received spreadsheet downloaded, prepared and copied, as an audio.
// Who may have it downloaded is decided before any I/O; Meta, the owner's reader and the private copy stay outside the transaction,
// which locks the contact and then the message and keeps what came of it once. A failure that may pass waits with the usual scale.
internal sealed class WhatsAppSpreadsheetDispatchService(IWhatsAppSpreadsheetReader reader, IWhatsAppSpreadsheetRepository messages,
    IWhatsAppConversationRepository conversations, WhatsAppSpreadsheetPreparationPolicy preparation, WhatsAppSpreadsheetReplyPolicy replies,
    IUnitOfWork unitOfWork, TimeProvider clock, ILogger<WhatsAppSpreadsheetDispatchService> logger) : IWhatsAppSpreadsheetDispatchService
{
    public Task<Result> PrepareAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "PrepareWhatsAppSpreadsheet", () => PrepareCoreAsync(contactId, cancellationToken));

    private async Task<Result> PrepareCoreAsync(Guid contactId, CancellationToken ct)
    {
        var source = await reader.FindPendingAsync(contactId, clock.GetUtcNow().UtcDateTime, ct);
        if (source is null) return Result.Success();
        var acquired = await preparation.CanDownloadAsync(source, ct) ? await preparation.AcquireAsync(source, ct) : null;
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await conversations.GetForUpdateAsync(contactId, token) is not { } contact) return Result.Success();
            var message = await messages.GetForUpdateAsync(contactId, source.MessageId, token);
            var current = await reader.FindAsync(contactId, source.MessageId, token);
            if (message is null || current is not { State: WhatsAppSpreadsheetState.Pending }) return Result.Success();
            // Losing the link, the mode or the permission while downloading leaves it as if it had never qualified.
            if (acquired is null || !await preparation.CanDownloadAsync(current, token))
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
}
