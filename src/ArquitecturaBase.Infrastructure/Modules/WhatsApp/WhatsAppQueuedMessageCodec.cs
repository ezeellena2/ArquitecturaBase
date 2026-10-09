using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>Closed wire format: database content never selects an arbitrary CLR type.</summary>
internal static class WhatsAppQueuedMessageCodec
{
    private sealed record Content(string To, string? Body = null, string? Footer = null, string? ButtonText = null, string? Url = null,
        IReadOnlyList<WhatsAppReplyButton>? Buttons = null, string? LanguageCode = null, string? Code = null,
        WhatsAppAccessEventKind? EventKind = null, Guid UserId = default, Guid InvitationId = default, string? Name = null, string? AppName = null, string? ReplyPayload = null);

    public static string Serialize(WhatsAppOutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var to = message.To.Value;
        Content content = message switch
        {
            WhatsAppTextMessage text => new(to, Body: text.Body),
            WhatsAppLinkButtonMessage link => new(to, Body: link.Body, Footer: link.Footer, ButtonText: link.ButtonText, Url: link.Url),
            WhatsAppReplyButtonsMessage buttons => new(to, Body: buttons.Body, Footer: buttons.Footer, Buttons: buttons.Buttons),
            WhatsAppLoginCodeMessage code => new(to, LanguageCode: code.LanguageCode, Code: code.Code, EventKind: code.AccessEventKind),
            WhatsAppInvitationMessage invitation => new(to, LanguageCode: invitation.LanguageCode, UserId: invitation.UserId, InvitationId: invitation.InvitationId, Name: invitation.Name, AppName: invitation.AppName, ReplyPayload: invitation.ReplyPayload),
            _ => throw new InvalidOperationException("The queued message type is not supported."),
        };
        return JsonSerializer.Serialize(content);
    }

    public static WhatsAppOutboundMessage Deserialize(string messageType, string payload)
    {
        var content = JsonSerializer.Deserialize<Content>(payload) ?? throw new InvalidOperationException("A delivery has no content.");
        var phone = PhoneNumber.Create(content.To);
        if (phone.IsFailure) throw new InvalidOperationException("A delivery has an invalid destination.");
        return messageType switch
        {
            nameof(WhatsAppTextMessage) => new WhatsAppTextMessage(phone.Value, content.Body!),
            nameof(WhatsAppLinkButtonMessage) => new WhatsAppLinkButtonMessage(phone.Value, content.Body!, content.ButtonText!, content.Url!, content.Footer),
            nameof(WhatsAppReplyButtonsMessage) => new WhatsAppReplyButtonsMessage(phone.Value, content.Body!, content.Buttons!, content.Footer),
            nameof(WhatsAppLoginCodeMessage) => new WhatsAppLoginCodeMessage(phone.Value, content.LanguageCode!, content.Code!, content.EventKind!.Value),
            nameof(WhatsAppInvitationMessage) => new WhatsAppInvitationMessage(phone.Value, content.UserId, content.InvitationId, content.LanguageCode!, content.Name!, content.AppName!, content.ReplyPayload!),
            _ => throw new InvalidOperationException("The queued message type is not supported."),
        };
    }
}
