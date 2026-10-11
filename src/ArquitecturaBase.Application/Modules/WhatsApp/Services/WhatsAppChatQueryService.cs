using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatQueryService(IWhatsAppChatReader reader, IWhatsAppFormService forms,
    IPhoneNumberParser phoneNumbers, ILogger<WhatsAppChatQueryService> logger) : IWhatsAppChatQueryService
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
        return formContact is null ? WhatsAppChatErrors.NotFound : new WhatsAppThreadResponse(new(ToResponse(row.Contact), formContact),
            [.. row.Messages.Select(message => new WhatsAppMessageResponse(message.Id, message.Direction, message.Kind,
                message.Purpose == WhatsAppMessagePurpose.Access ? null : message.Body, message.OccurredAtUtc,
                message.InboundSequence, message.OperatorUserId, message.Status, message.ErrorCode, message.Purpose, message.AccessEventKind,
                ToFormResponse(message.Form)))],
            [.. row.Outgoing.Select(ToOutgoingResponse)], row.BeforeAtUtc, row.BeforeId);
    });

    public Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contactId, Guid requestId,
        Guid actor, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger, "FindWhatsAppOutgoingQuery", async () =>
    {
        var outgoing = await reader.FindOutgoingAsync(contactId, requestId, actor, cancellationToken);
        if (outgoing is null) return WhatsAppChatErrors.NotFound;
        return ToOutgoingResponse(outgoing);
    });

    private static WhatsAppOutgoingMessageResponse ToOutgoingResponse(WhatsAppOutgoingMessageRow row) =>
        new(row.Id, row.RequestId, row.OperatorUserId, row.Body, row.RequestedAtUtc,
            row.State, row.WaMessageId, ToFormResponse(row.Form), Kind: row.Kind);

    private static WhatsAppFormPresentationResponse? ToFormResponse(WhatsAppFormPresentationRow? row)
    {
        if (row is null) return null;
        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored Flow form name is invalid.");
        return new(row.DefinitionId, row.VersionId, row.VersionNumber, name, row.Culture);
    }

    private WhatsAppChatResponse ToResponse(WhatsAppChatRow row)
    {
        var parsed = WhatsAppIds.Parse(phoneNumbers, row.PhoneNumber);
        return new(row.Id, row.ProfileName, parsed.IsSuccess ? phoneNumbers.FormatInternational(parsed.Value) : null,
            row.Mode, row.ModeRevision, row.LastInboundAtUtc, row.LastInboundSequence, row.UnreadCount,
            row.PreviewPurpose == WhatsAppMessagePurpose.Access ? null : row.Preview, row.PreviewPurpose, row.PreviewAccessEventKind);
    }
}
