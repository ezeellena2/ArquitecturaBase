using System.Text.Json;
using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatQueryService(IWhatsAppChatReader reader, IWhatsAppFormService forms,
    IStructuredRecordSource recordSource,
    IPhoneNumberParser phoneNumbers, IAssistantOperations assistant, WhatsAppAudioPresentationPolicy audio, WhatsAppAssistantPresentationPolicy presentation, ILogger<WhatsAppChatQueryService> logger) : IWhatsAppChatQueryService
{
    public Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppChatsResponse>>(logger, "ListWhatsAppChatsQuery", async () =>
    {
        var page = await reader.ListChatsAsync(request, actor, cancellationToken);
        var counts = await reader.CountChatsAsync(request, actor, cancellationToken);
        return new WhatsAppChatsResponse(new PagedResult<WhatsAppChatResponse>(
            [.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.TotalCount), counts.AllCount, counts.UnreadCount, counts.NeedsPersonCount);
    });

    public Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, Guid actor,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppThreadResponse>>(logger, "GetWhatsAppThreadQuery", async () =>
    {
        var row = await reader.FindThreadAsync(request, actor, cancellationToken);
        if (row is null) return WhatsAppChatErrors.NotFound;
        var formContact = await forms.FindContactAsync(request.ContactId, cancellationToken);
        if (formContact is null) return WhatsAppChatErrors.NotFound;
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var receiptIds = row.Messages.Where(message => message.FormReply is { State: "Completed", SourceReceiptId: not null })
            .Select(message => message.FormReply!.SourceReceiptId!.Value).Distinct().ToArray();
        var availability = await assistant.GetAvailabilityAsync(cancellationToken);
        var pointer = await presentation.FindCurrentAsync(request.ContactId, actor, cancellationToken);
        var recordResult = await recordSource.ReadAsync(actor, culture, receiptIds, cancellationToken);
        // Review of task 9: the forms of «Por qué lo propuso» and «Por qué preguntó», named in the reader's language as they are now.
        var formNames = row.Messages.Any(message => message.Document?.Why is not null || message.SpreadsheetRowsJson is not null)
            || row.Outgoing.Any(item => item.SpreadsheetRowsJson is not null)
            ? await presentation.FormNamesAsync(culture, cancellationToken) : new Dictionary<Guid, string>();
        return new WhatsAppThreadResponse(new(ToResponse(row.Contact), formContact),
            [.. row.Messages.Select(message => new WhatsAppMessageResponse(message.Id, message.Direction, message.Kind,
                message.Purpose == WhatsAppMessagePurpose.Access || (message.Origin == WhatsAppMessageOrigin.FormAction || message.LoadsFile) && !recordResult.CanReadRecords
                    ? null : audio.VisibleText(message), message.OccurredAtUtc,
                message.InboundSequence, message.OperatorUserId, message.Status, message.ErrorCode, message.Purpose, message.AccessEventKind,
                ToFormResponse(message.Form), ToFormReplyResponse(message.FormReply, recordResult, message.OccurredAtUtc), message.Origin, Assistant: presentation.Explain(message.Assistant, recordResult.CanReadRecords),
                QuickReplies: WhatsAppAssistantPresentationPolicy.Titles(message.QuickReplyTitlesJson, false), Audio: audio.View(message),
                // A received spreadsheet shows its name, and its caption as the body (stage 2 of the assistant authoring).
                SpreadsheetName: message.Kind == WhatsAppMessageKind.Spreadsheet ? message.SpreadsheetName : null,
                // W1-A: a received location shows its point.
                Location: message.Kind == WhatsAppMessageKind.Location && message.Latitude is { } latitude && message.Longitude is { } longitude
                    ? new WhatsAppLocationPointResponse(latitude, longitude) : null,
                // W3: a shared contact shows the name and the phones of its first card; the address of a place stays out of the chat.
                Contact: ToContactResponse(message),
                AssistantReply: WhatsAppOutgoingPresentationPolicy.Reply(message.AssistantReplyJson),
                // Task 8 of the documents: a received document shows its name, kind and pages, and its caption as the body.
                Document: WhatsAppReceivedDocumentPresentationPolicy.View(message.Kind, message.Document, presentation, recordResult.CanReadRecords, formNames, culture),
                // Review of task 9: the button of a question that went as a list, and the mark of what was sent for a file.
                ListButton: message.ListButton, LoadsFile: message.LoadsFile,
                // W3 (task 24): «Prellenado con» of a resend that went out, with what the send had taken when it left.
                Prefill: presentation.Prefill(message.SharedPrefill, recordResult.CanReadRecords, message.OccurredAtUtc),
                // VC1-A (task 29): «Por qué lo propuso» of the notice of the loads, its forms named in the reader's language as they are now.
                SpreadsheetRows: WhatsAppSpreadsheetRowsCodec.Read(message.SpreadsheetRowsJson, formNames),
                // H9: what went out and Meta later reported as failed says why.
                FailureCode: message.Direction == WhatsAppMessageDirection.Outbound && message.Status == WhatsAppMessageStatus.Failed
                    && message.Purpose != WhatsAppMessagePurpose.Access ? WhatsAppSendErrors.FromMetaCode(message.ErrorCode)?.Code : null))],
            [.. row.Outgoing.Select(item => ToOutgoingResponse(item, recordResult.CanReadRecords, formNames))], row.BeforeAtUtc, row.BeforeId, recordResult.CanReadRecords)
            {
                AssistantAvailability = availability, AssistantSuggestion = pointer,
                // Casos del asistente: what ended without a message is a line of the chat; one whose why does not read is left out.
                AssistantEvents = row.AssistantEvents.Select(item => WhatsAppOutgoingPresentationPolicy.Reply(item.OutcomeJson) is { } why
                    ? new WhatsAppAssistantEventResponse(item.RequestId, item.AtUtc, why) : null).OfType<WhatsAppAssistantEventResponse>().ToArray() is { Length: > 0 } lines
                    ? lines : null,
            };
    });

    public Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId,
        Guid actor, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger, "FindWhatsAppOutgoingQuery", async () =>
    {
        var outgoing = await reader.FindOutgoingAsync(contactId, requestId, actor, cancellationToken);
        if (outgoing is null) return WhatsAppChatErrors.NotFound;
        var access = await recordSource.ReadAsync(actor, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es", [], cancellationToken);
        return ToOutgoingResponse(outgoing, access.CanReadRecords);
    });

    private WhatsAppOutgoingMessageResponse ToOutgoingResponse(WhatsAppOutgoingMessageRow row, bool canReadRecords = true,
        IReadOnlyDictionary<Guid, string>? formNames = null) =>
        new(row.Id, row.RequestId, row.OperatorUserId, (row.Origin == WhatsAppMessageOrigin.FormAction || row.LoadsFile) && !canReadRecords ? null : row.Body, row.RequestedAtUtc,
            row.State, row.WaMessageId, ToFormResponse(row.Form), Kind: WhatsAppOutgoingPresentationPolicy.Kind(row.Kind), Origin: row.Origin, Assistant: presentation.Explain(row.Assistant, canReadRecords),
            QuickReplies: row.Origin == WhatsAppMessageOrigin.FormAction && !canReadRecords ? null : WhatsAppAssistantPresentationPolicy.Titles(row.QuickRepliesJson, true),
            AssistantReply: WhatsAppOutgoingPresentationPolicy.Reply(row.AssistantReplyJson), ListButton: row.ListButton, LoadsFile: row.LoadsFile,
            // W3 (task 24): a resend that waits in the queue carries everything the send took; one that left it without going out (failed,
            // uncertain or reviewed) says what it was queued with.
            Prefill: presentation.Prefill(row.SharedPrefill, canReadRecords,
                row.State is WhatsAppOutgoingMessageState.Pending or WhatsAppOutgoingMessageState.Sending ? null : row.RequestedAtUtc),
            SpreadsheetRows: WhatsAppSpreadsheetRowsCodec.Read(row.SpreadsheetRowsJson, formNames ?? new Dictionary<Guid, string>()),
            // H2: who reviewed a send of the chat; a notice's reviewer belongs to the forms' review, not to the thread.
            ReviewedByUserId: IsReviewedSend(row) ? row.ReviewedByUserId : null,
            ReviewedAtUtc: IsReviewedSend(row) ? row.ReviewedAtUtc : null,
            FailureCode: row.State == WhatsAppOutgoingMessageState.Failed ? row.FailureCode : null);

    private static bool IsReviewedSend(WhatsAppOutgoingMessageRow row) =>
        row.Origin == WhatsAppMessageOrigin.Conversation && row.State == WhatsAppOutgoingMessageState.Reviewed;

    // W3: the card of a shared contact while its content lives. One whose phones do not read, or that the front's closed contract would
    // refuse, is left out like a why that does not read: one bad row does not stop the thread.
    private static WhatsAppSharedContactResponse? ToContactResponse(WhatsAppMessageRow message)
    {
        if (message.Kind != WhatsAppMessageKind.Contact || message.ContactPhonesJson is not { } phonesJson) return null;
        string?[]? phones;
        try { phones = JsonSerializer.Deserialize<string?[]>(phonesJson); }
        catch (JsonException) { return null; }
        return phones is { Length: <= WhatsAppMessage.MaxContactPhones } && phones.All(phone => phone is { Length: > 0 })
            ? new(string.IsNullOrEmpty(message.ContactName) ? null : message.ContactName, [.. phones.OfType<string>()])
            : null;
    }

    private static WhatsAppFormPresentationResponse? ToFormResponse(WhatsAppFormPresentationRow? row)
    {
        if (row is null) return null;
        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored Flow form name is invalid.");
        return new(row.DefinitionId, row.VersionId, row.VersionNumber, name, row.Culture) { Location = WhatsAppOutgoingPresentationPolicy.Location(row.Location) };
    }

    private static WhatsAppFormReplyResponse? ToFormReplyResponse(WhatsAppFormReplyRow? row,
        StructuredRecordReadResult recordResult, DateTime receivedAtUtc)
    {
        if (row is null) return null;
        StructuredRecordReadReply? record = null;
        if (row.State == "Completed" && recordResult.CanReadRecords && row.SourceReceiptId is { } receiptId)
            recordResult.Replies.TryGetValue(receiptId, out record);
        string? name = record?.Name;
        var recognized = row.State != "Unrecognized";
        if (record is null && recognized && row.NameJson is { } nameJson)
        {
            var names = JsonSerializer.Deserialize<Dictionary<string, string>>(nameJson, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("The stored Flow reply name is invalid.");
            var uiCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            names.TryGetValue(row.Culture is "es" or "en" ? row.Culture : uiCulture, out name);
        }
        var values = record?.Values.Select(value => new WhatsAppFormReplyValueResponse(value.FieldId, value.Key, value.Type, value.Label, value.Value)).ToArray();
        return new(row.State, recognized ? row.DefinitionId : null, recognized ? row.VersionId : null,
            recognized ? row.VersionNumber : null, name,
            row.State == "Completed" ? row.RecordId : null,
            row.State == "Completed" ? row.Number : null,
            values, row.State == "Rejected" ? row.RejectionCode : null, row.State == "Rejected" ? row.RejectedQuestion : null, row.FileCount, row.FileCount.HasValue ? receivedAtUtc : null)
        {
            Amendment = record?.Amendment,
            // W3 (task 24): the second response of a send says the number of the one it kept.
            SavedNumber = row.State == "Rejected" ? row.SavedNumber : null,
        };
    }

    private WhatsAppChatResponse ToResponse(WhatsAppChatRow row)
    {
        var parsed = WhatsAppIds.Parse(phoneNumbers, row.PhoneNumber);
        // Like the audio's, a spreadsheet's preview is generic: neither its name nor its caption.
        var preview = row.PreviewIsAssistant ? FlowTexts.Get("AssistantPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es") : row.PreviewKind == WhatsAppMessageKind.Audio ? FlowTexts.Get("AudioPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.PreviewKind == WhatsAppMessageKind.Spreadsheet ? FlowTexts.Get("SpreadsheetPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            // W3: a shared contact's preview is generic too, without its name or its phones.
            : row.PreviewKind == WhatsAppMessageKind.Contact ? FlowTexts.Get("ContactPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.PreviewKind == WhatsAppMessageKind.ReceivedDocument ? FlowTexts.Get("DocumentPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es") : row.PreviewIsFormAction
            ? FlowTexts.Get("FormActionPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.PreviewKind == WhatsAppMessageKind.FlowReply
            ? FlowTexts.Get("FormReplyPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.Preview;
        return new(row.Id, row.ProfileName, parsed.IsSuccess ? phoneNumbers.FormatInternational(parsed.Value) : null,
            row.Mode, row.ModeRevision, row.LastInboundAtUtc, row.LastInboundSequence, row.UnreadCount,
            row.PreviewPurpose == WhatsAppMessagePurpose.Access ? null : preview, row.PreviewPurpose, row.PreviewAccessEventKind,
            row.HandoffAtUtc is { } handoffAtUtc && row.HandoffReasonCode is { } reasonCode ? new(reasonCode, row.HandoffReason, handoffAtUtc) : null);
    }
}
