using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioAccessPolicy(IUserReader users, IPermissionService permissions, ICurrentUser current,
    IWhatsAppFormRecipientReader recipients, ISystemSettingsReader settings, WhatsAppAssistantOfferCapacityPolicy capacity)
{
    public Task<bool> HasOfferCapacityAsync(Guid contactId, AssistantChannelContext context, CancellationToken ct) =>
        context.InteractionMode == AssistantInteractionMode.Assistant ? capacity.HasCapacityAsync(contactId, context, ct) : Task.FromResult(true);

    public async Task<Result<Guid>> RequireOperatorAsync(CancellationToken ct)
    {
        if (current.UserId is not {} actor) return WhatsAppChatErrors.SessionRequired;
        return await AuthorizeAsync(actor,ct) ? actor : WhatsAppChatErrors.Forbidden;
    }
    private async Task<bool> AuthorizeAsync(Guid actor,CancellationToken ct)=>
        await users.FindByIdAsync(actor,ct) is {IsActive:true} && await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Read,ct);
    public async Task<Result<AssistantChannelContext>> CaptureAsync(WhatsAppAudioSourceRow source, Guid? operatorUserId, bool continuing, CancellationToken ct)
    {
        var automatic=operatorUserId is null;
        if(!continuing && source.Mode!=(automatic?WhatsAppConversationMode.Assistant:WhatsAppConversationMode.Manual))return WhatsAppChatErrors.RequestConflict;
        if(operatorUserId is {} actor && !await AuthorizeAsync(actor,ct))return WhatsAppChatErrors.Forbidden;
        var recipient=await recipients.FindAsync(source.ContactId,ct);
        var subject=recipient is {IsActive:true,UserId:not null} && recipient.UserId==source.LinkedUserId && recipient.LinkRevision==source.LinkRevision ? recipient.UserId : null;
        if(automatic)
        {
            if(subject is not {} subjectId || await users.FindByIdAsync(subjectId,ct) is not {IsActive:true,PhoneNumberConfirmed:true} account
                || recipient?.WaId is null || account.PhoneNumber!="+"+recipient.WaId)return WhatsAppChatErrors.Forbidden;
        }
        var presentation=await settings.FindPresentationAsync(ct);
        var culture=subject is not null && recipient?.Culture is "es" or "en" ? recipient.Culture : presentation.DefaultCulture;
        if(culture is not ("es" or "en") || !TimeZoneInfo.TryFindSystemTimeZoneById(presentation.DefaultTimeZoneId,out _))return WhatsAppChatErrors.RequestConflict;
        return new AssistantChannelContext(automatic?"whatsapp-assistant":"whatsapp-manual",operatorUserId??subject!.Value,subject,culture,presentation.DefaultTimeZoneId,
            new(automatic?[]:[WhatsAppPermissions.Read],[]),[],[])
        { InteractionMode = automatic ? AssistantInteractionMode.Assistant : AssistantInteractionMode.Manual };
    }
    public async Task<Result<AssistantChannelContext>> RevalidateAsync(WhatsAppAudioSourceRow source,CancellationToken ct)
    {
        if(source.CapturedLinkRevision!=source.LinkRevision || source.ActorUserId is not {} actor || source.ContextJson is null)return WhatsAppChatErrors.RequestConflict;
        AssistantChannelContext? captured;
        try {captured=JsonSerializer.Deserialize<AssistantChannelContext>(source.ContextJson,JsonSerializerOptions.Web);}
        catch(JsonException){return WhatsAppChatErrors.RequestConflict;}
        if(captured is null || captured.ActorUserId!=actor
            || captured.InteractionMode is not (AssistantInteractionMode.Assistant or AssistantInteractionMode.Manual)
            || captured.ChannelId != (captured.InteractionMode == AssistantInteractionMode.Manual ? "whatsapp-manual" : "whatsapp-assistant")
            || captured.AllowedCapabilities is not {Count:0} || captured.AllowedTerminals is not {Count:0}
            || captured.RequiredPermissions?.Actor is null || captured.RequiredPermissions.Subject is not {Count:0}
            || !captured.RequiredPermissions.Actor.SequenceEqual(captured.InteractionMode==AssistantInteractionMode.Manual?[WhatsAppPermissions.Read]:Array.Empty<string>()))return WhatsAppChatErrors.RequestConflict;
        var currentContext=await CaptureAsync(source,captured.InteractionMode==AssistantInteractionMode.Manual?actor:null,true,ct);
        return currentContext.IsFailure?currentContext.Error:currentContext.Value.ActorUserId==actor && currentContext.Value.SubjectUserId==captured.SubjectUserId
            ?captured:WhatsAppChatErrors.RequestConflict;
    }
}
