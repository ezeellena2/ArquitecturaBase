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
    IPhoneNumberParser phoneNumbers, IAssistantOperations assistant, WhatsAppAudioPresentationPolicy audio, ILogger<WhatsAppChatQueryService> logger) : IWhatsAppChatQueryService
{
    public Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppChatsResponse>>(logger, "ListWhatsAppChatsQuery", async () =>
    {
        var page = await reader.ListChatsAsync(request, actor, cancellationToken);
        var counts = await reader.CountChatsAsync(request, actor, cancellationToken);
        return new WhatsAppChatsResponse(new PagedResult<WhatsAppChatResponse>(
            [.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.TotalCount), counts.AllCount, counts.UnreadCount);
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
        var recordResult = await recordSource.ReadAsync(actor, culture, receiptIds, cancellationToken);
        var availability = await assistant.GetAvailabilityAsync(cancellationToken);
        return new WhatsAppThreadResponse(new(ToResponse(row.Contact), formContact),
            [.. row.Messages.Select(message => new WhatsAppMessageResponse(message.Id, message.Direction, message.Kind,
                message.Purpose == WhatsAppMessagePurpose.Access || message.Origin == WhatsAppMessageOrigin.FormAction && !recordResult.CanReadRecords ? null : audio.VisibleText(message), message.OccurredAtUtc,
                message.InboundSequence, message.OperatorUserId, message.Status, message.ErrorCode, message.Purpose, message.AccessEventKind,
                ToFormResponse(message.Form), ToFormReplyResponse(message.FormReply, recordResult, message.OccurredAtUtc), message.Origin, Audio: audio.View(message)))],
            [.. row.Outgoing.Select(item => ToOutgoingResponse(item, recordResult.CanReadRecords))], row.BeforeAtUtc, row.BeforeId, recordResult.CanReadRecords)
            { AssistantAvailability = availability };
    });

    public Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId,
        Guid actor, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger, "FindWhatsAppOutgoingQuery", async () =>
    {
        var outgoing = await reader.FindOutgoingAsync(contactId, requestId, actor, cancellationToken);
        if (outgoing is null) return WhatsAppChatErrors.NotFound;
        return ToOutgoingResponse(outgoing);
    });

    private static WhatsAppOutgoingMessageResponse ToOutgoingResponse(WhatsAppOutgoingMessageRow row, bool canReadRecords = true) =>
        new(row.Id, row.RequestId, row.OperatorUserId, row.Origin == WhatsAppMessageOrigin.FormAction && !canReadRecords ? null : row.Body, row.RequestedAtUtc,
            row.State, row.WaMessageId, ToFormResponse(row.Form), Kind: row.Kind, Origin: row.Origin);

    private static WhatsAppFormPresentationResponse? ToFormResponse(WhatsAppFormPresentationRow? row)
    {
        if (row is null) return null;
        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored Flow form name is invalid.");
        return new(row.DefinitionId, row.VersionId, row.VersionNumber, name, row.Culture);
    }

    private static WhatsAppFormReplyResponse? ToFormReplyResponse(WhatsAppFormReplyRow? row,
        StructuredRecordReadResult recordResult, DateTime receivedAtUtc)
    {
        if (row is null) return null;
        string? name = null;
        var recognized = row.State != "Unrecognized";
        if (recognized && row.NameJson is { } nameJson)
        {
            var names = JsonSerializer.Deserialize<Dictionary<string, string>>(nameJson, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("The stored Flow reply name is invalid.");
            names.TryGetValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es", out name);
        }
        var values = row.State == "Completed" && recordResult.CanReadRecords && row.SourceReceiptId is { } receiptId
            && recordResult.Replies.TryGetValue(receiptId, out var record)
            ? record.Values.Select(value => new WhatsAppFormReplyValueResponse(value.FieldId, value.Key, value.Type, value.Label, value.Value)).ToArray()
            : null;
        return new(row.State, recognized ? row.DefinitionId : null, recognized ? row.VersionId : null,
            recognized ? row.VersionNumber : null, name,
            row.State == "Completed" ? row.RecordId : null,
            row.State == "Completed" ? row.Number : null,
            values, row.State == "Rejected" ? row.RejectionCode : null, row.State == "Rejected" ? row.RejectedQuestion : null, row.FileCount, row.FileCount.HasValue ? receivedAtUtc : null);
    }

    private WhatsAppChatResponse ToResponse(WhatsAppChatRow row)
    {
        var parsed = WhatsAppIds.Parse(phoneNumbers, row.PhoneNumber);
        var preview = row.PreviewIsAudio ? FlowTexts.Get("AudioPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es") : row.PreviewIsFormAction
            ? FlowTexts.Get("FormActionPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.PreviewIsFormReply
            ? FlowTexts.Get("FormReplyPreview", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es")
            : row.Preview;
        return new(row.Id, row.ProfileName, parsed.IsSuccess ? phoneNumbers.FormatInternational(parsed.Value) : null,
            row.Mode, row.ModeRevision, row.LastInboundAtUtc, row.LastInboundSequence, row.UnreadCount,
            row.PreviewPurpose == WhatsAppMessagePurpose.Access ? null : preview, row.PreviewPurpose, row.PreviewAccessEventKind);
    }
}
