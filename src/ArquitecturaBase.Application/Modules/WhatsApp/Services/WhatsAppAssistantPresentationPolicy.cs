using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAssistantPresentationPolicy(IWhatsAppFlowSessionProtector protector,IWhatsAppAssistantReader references,
    IStructuredRecordSource records,IPermissionService permissions,TimeProvider clock)
{
    public WhatsAppAssistantExplanationResponse? Explain(WhatsAppAssistantPresentationRow? row,bool canReadRecords)
    {
        if(!canReadRecords || row is null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)return null;
        var content=protector.ReadAssistant(row.SessionId,row.ContactId,row.UserId,row.VersionId,row.ProtectedContent);
        return content.Origin.Reason is {Length:>0} reason ? new(reason,content.ProposedFields) : null;
    }
    public async Task<WhatsAppAssistantExplanationResponse?> ReadAsync(Guid actor,WhatsAppAssistantPresentationRow? row,CancellationToken ct)
    {
        if(row is null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)return null;
        var access=await records.ReadAsync(actor,CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"en":"es",[],ct);
        return Explain(row,access.CanReadRecords);
    }
    public async Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId,Guid actor,CancellationToken ct)
    {
        if(!await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Read,ct)
            || !await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Send,ct))return null;
        var pointer=await references.FindCurrentAsync(contactId,ct);
        return await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Send,ct)?pointer:null;
    }
    public static IReadOnlyList<string>? Titles(string? json,bool outgoing)
    {
        if(json is null)return null;
        return outgoing ? JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(json,JsonSerializerOptions.Web)?.Select(reply=>reply.Title).ToArray()
            : JsonSerializer.Deserialize<string[]>(json,JsonSerializerOptions.Web);
    }
    public static WhatsAppAssistantPresentationRow? Row(WhatsAppFlowSession session)=>
        session.ProtectedAssistantContent is {} content && session.AssistantContentExpiresAtUtc is {} expiry
            ?new(session.Id,session.ContactId,session.UserId,session.VersionId,content,expiry):null;
}
