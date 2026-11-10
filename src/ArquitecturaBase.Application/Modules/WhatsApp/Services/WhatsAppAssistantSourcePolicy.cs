using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// IA, Stage 2: a stored spreadsheet is a source and its caption is the request. Without a caption it pairs with the contiguous text,
// or audio with its transcription, before or after it, when no assistant reply was requested in between and they arrived less than
// ten minutes apart: the request is the text, and the spreadsheet goes with it. A text waits for its spreadsheet while it is
// downloaded, and is not interpreted when it was rejected, since the rejection answered; an unavailable one leaves it alone. The
// reader resolves its sources with this rule; a spreadsheet alone is interpreted only as the answer its conversation waits for.
public static class WhatsAppAssistantSourcePolicy
{
    public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(10);

    public static WhatsAppAssistantSourceRow Resolve(WhatsAppAssistantSourceRow source, WhatsAppAssistantNeighborRow? previous)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CapturedRecordId is not null) return source;
        var pair = previous is { AnsweredSince: false } && source.OccurredAtUtc - previous.OccurredAtUtc < PairingWindow ? previous : null;
        if (source.Kind == WhatsAppMessageKind.Spreadsheet)
        {
            if (!string.IsNullOrWhiteSpace(source.Body)) return source;
            if (pair is { Kind: WhatsAppMessageKind.Text or WhatsAppMessageKind.Audio } && !string.IsNullOrWhiteSpace(pair.Body))
                return source with { Body = pair.Body, ContentExpiresAtUtc = Earliest(source.ContentExpiresAtUtc, pair.ContentExpiresAtUtc) };
            return source with { Body = "", State = pair is { Kind: WhatsAppMessageKind.Audio, AudioState: WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing }
                ? WhatsAppAssistantSourceState.Waiting : WhatsAppAssistantSourceState.Alone };
        }
        if (pair is not { Kind: WhatsAppMessageKind.Spreadsheet } || !string.IsNullOrWhiteSpace(pair.Body)) return source;
        return pair.SpreadsheetState switch
        {
            WhatsAppSpreadsheetState.Pending => source with { State = WhatsAppAssistantSourceState.Waiting },
            WhatsAppSpreadsheetState.Rejected => source with { State = WhatsAppAssistantSourceState.Answered },
            WhatsAppSpreadsheetState.Stored when pair.Spreadsheet is { } sheet =>
                source with { Spreadsheet = sheet, ContentExpiresAtUtc = Earliest(source.ContentExpiresAtUtc, sheet.ExpiresAtUtc) },
            _ => source,
        };
    }

    /// <summary>
    /// The spreadsheet as the engine takes it: the private copy, with the name the message keeps (the one its owner read) and the expiry
    /// of its message's content, and the prepared sheet. The prepared sheet was serialized by this module: one that does not read is a fault.
    /// </summary>
    public static AssistantAttachmentInput Attachment(WhatsAppAssistantSpreadsheetRow sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        StructuredSpreadsheet? prepared;
        try { prepared = JsonSerializer.Deserialize<StructuredSpreadsheet>(sheet.PreparedJson, JsonSerializerOptions.Web); }
        catch (JsonException exception) { throw new InvalidOperationException("The stored prepared spreadsheet is invalid.", exception); }
        if (prepared is null) throw new InvalidOperationException("The stored prepared spreadsheet is missing.");
        return new(sheet.StorageKey, WhatsAppMessage.SpreadsheetName(sheet.FileName, prepared.MediaType), prepared.MediaType, sheet.Size, sheet.Sha256,
            sheet.ExpiresAtUtc, prepared);
    }

    private static DateTime Earliest(DateTime first, DateTime second) => first <= second ? first : second;
}
