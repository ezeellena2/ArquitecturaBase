using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppOutgoingPresentationPolicy
{
    public static WhatsAppFormPresentationResponse? Form(WhatsAppFormPresentationRow? row)
    {
        if (row is null) return null;
        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored Flow form name is invalid.");
        return new(row.DefinitionId,row.VersionId,row.VersionNumber,name,row.Culture) { Location = Location(row.Location) };
    }
    /// <summary>W1-A: how the send of a location request stands.</summary>
    public static WhatsAppFlowLocationResponse? Location(WhatsAppFlowLocationRow? row) =>
        row is null || row.State == WhatsAppFlowLocationState.None ? null : new(row.State.ToString(), row.ExpiresAtUtc);
    public static WhatsAppOutgoingMessageResponse Map(WhatsAppOutgoingMessageRow row,WhatsAppAssistantExplanationResponse? explanation = null) =>
        new(row.Id,row.RequestId,row.OperatorUserId,row.Body,row.RequestedAtUtc,row.State,row.WaMessageId,Form(row.Form),Kind:row.Kind,Origin:row.Origin,Assistant:explanation,QuickReplies:WhatsAppAssistantPresentationPolicy.Titles(row.QuickRepliesJson,true),
            AssistantReply:Reply(row.AssistantReplyJson));
    /// <summary>Casos del asistente: «Por qué respondió esto» as the chat shows it, or nothing when it does not read.</summary>
    public static WhatsAppAssistantReplyResponse? Reply(string? json) =>
        WhatsAppAssistantReplyRecordCodec.Parse(json) is { } record ? WhatsAppAssistantReplyResponse.From(record) : null;
    public static WhatsAppOutgoingMessageResponse Map(WhatsAppOutgoingMessage row,WhatsAppFlowSession session,WhatsAppAssistantExplanationResponse? explanation = null) =>
        new(row.Id,row.RequestId,row.OperatorUserId,row.Body,row.RequestedAtUtc,row.State,row.WaMessageId,
            Form(new(session.DefinitionId,session.VersionId,session.VersionNumber,session.NameJson,session.Culture)
                { Location = row.Kind == WhatsAppOutgoingMessageKind.LocationRequest ? new(session.LocationState, session.ExpiresAtUtc) : null }),
            Kind:row.Kind,Origin:row.Origin,Assistant:explanation,QuickReplies:WhatsAppAssistantPresentationPolicy.Titles(row.QuickRepliesJson,true));
}
