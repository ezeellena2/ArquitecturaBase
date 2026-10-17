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
        return new(row.DefinitionId,row.VersionId,row.VersionNumber,name,row.Culture);
    }
    public static WhatsAppOutgoingMessageResponse Map(WhatsAppOutgoingMessageRow row) =>
        new(row.Id,row.RequestId,row.OperatorUserId,row.Body,row.RequestedAtUtc,row.State,row.WaMessageId,Form(row.Form),Kind:row.Kind,Origin:row.Origin);
    public static WhatsAppOutgoingMessageResponse Map(WhatsAppOutgoingMessage row,WhatsAppFlowSession session) =>
        new(row.Id,row.RequestId,row.OperatorUserId,row.Body,row.RequestedAtUtc,row.State,row.WaMessageId,
            Form(new(session.DefinitionId,session.VersionId,session.VersionNumber,session.NameJson,session.Culture)),Kind:row.Kind,Origin:row.Origin);
}
