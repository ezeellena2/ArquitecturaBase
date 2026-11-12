using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantAccessPolicy(IUserReader users, IPermissionService permissions, ICurrentUser currentUser,
    IWhatsAppFormRecipientReader recipients, WhatsAppFormActionPolicy recipientGuard, ISystemSettingsReader settings, TimeProvider clock,
    IWhatsAppAssistantAgentReader agents)
{
    public async Task<Result<UserAccount>> RequireOperatorAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return WhatsAppAssistantErrors.SessionRequired;
        return await AuthorizeOperatorAsync(actor, ct);
    }

    private async Task<Result<UserAccount>> AuthorizeOperatorAsync(Guid actor, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(actor, ct);
        if (account is not { IsActive: true } || !await permissions.HasPermissionAsync(actor, WhatsAppPermissions.Read, ct)
            || !await permissions.HasPermissionAsync(actor, WhatsAppPermissions.Send, ct)) return WhatsAppAssistantErrors.Forbidden;
        return account;
    }

    public async Task<Result<WhatsAppAssistantContextResponse>> CaptureAsync(WhatsAppContact contact, Guid? operatorUserId, CancellationToken ct)
    {
        var automatic = operatorUserId is null;
        if (contact.Mode != (automatic ? WhatsAppConversationMode.Assistant : WhatsAppConversationMode.Manual))
            return WhatsAppAssistantErrors.StateConflict;
        var recipient = await recipients.FindAsync(contact.Id, ct);
        if (recipient?.UserId is not { } subject || recipient.UserId != contact.UserId || !recipient.IsActive
            || recipient.Culture is not ("es" or "en")
            || await recipientGuard.ValidateAsync(contact, subject, null, ct) is not null) return WhatsAppAssistantErrors.Unavailable;
        if (operatorUserId is { } actor && (await AuthorizeOperatorAsync(actor, ct)).IsFailure) return WhatsAppAssistantErrors.Forbidden;
        var presentation = await settings.FindPresentationAsync(ct);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(presentation.DefaultTimeZoneId, out _)) return WhatsAppAssistantErrors.Unavailable;
        var actorUserId = operatorUserId ?? subject;
        var context = new AssistantChannelContext(automatic ? "whatsapp-assistant" : "whatsapp-manual", actorUserId, subject, recipient.Culture,
            presentation.DefaultTimeZoneId, new(automatic ? [] : [WhatsAppPermissions.Read, WhatsAppPermissions.Send], []),
            [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1),
                new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1), new("whatsapp.decline-form", 1),
                new("forms.missing-request.propose",1)],
            [new("assistant_proposal", 1)])
        { InteractionMode = automatic ? AssistantInteractionMode.Assistant : AssistantInteractionMode.Manual,
            ChannelConversationId = automatic ? null : contact.Id };
        // Only the person's own conversation builds forms and catalogs (IA3-A); each one is offered by its own permissions. A catalog
        // maps the conversation's spreadsheet (version 2) and an import needs one: without a spreadsheet the engine leaves it out, so
        // a turn offers 15 definitions, or 16 with a spreadsheet, the cap (IA, Stage 2).
        if(automatic)context=context with {AllowedCapabilities=[..context.AllowedCapabilities,new("forms.catalog-filters.metadata",1),new("forms.catalog-filters.propose",1),
            new("forms.authoring.forms",1),new("forms.authoring.catalogs",1),new("forms.authoring.propose-form",2),new("forms.authoring.propose-catalog",2),
            new("forms.authoring.propose-import",1),new("forms.authoring.decline",1)]};
        if (await agents.FindMainAsync(ct) is not null) context = WithAgent(context, automatic);
        return new WhatsAppAssistantContextResponse(context, WhatsAppFormRecipientRevision.Create(actorUserId, recipient));
    }

    /// <summary>
    /// Casos del asistente: with a saved agent the engine preloads the agent, the form candidates and the conversation's history, so a
    /// common turn costs a proposal and the terminal. The agent's reply replaces «No responder», a draft and the knowledge it needs come
    /// in, and the offers come with the history, which is what a decline cites. The automatic channel closes on up to three proposals,
    /// in order; Atención manual keeps choosing one suggestion. With a spreadsheet and a profile qualified to draft it is 16 definitions.
    /// </summary>
    public static AssistantChannelContext WithAgent(AssistantChannelContext context, bool automatic)
    {
        AssistantCapabilityReference[] preloaded = [new("whatsapp.agent", 1), new("forms.assistant-candidates", 1), new("whatsapp.agent-history", 1)];
        AssistantCapabilityReference[] replaced = [new("whatsapp.no-reply", 1), new("whatsapp.offered-forms", 1), .. preloaded];
        return context with
        {
            PreloadedCapabilities = preloaded,
            AllowedCapabilities = [.. context.AllowedCapabilities.Where(capability => !replaced.Contains(capability)),
                new("whatsapp.reply", 1), new("whatsapp.draft-reply", 1), new("whatsapp.agent-knowledge", 1)],
            AllowedTerminals = [new("assistant_proposal", automatic ? 2 : 1)],
        };
    }

    // A suggestion after a capture (C4) keeps its own contract, with or without an agent: its tools, no preloads and one proposal.
    public static AssistantChannelContext ForCapturedRecord(AssistantChannelContext context, Guid recordId)=>context with
    {
        SourceRecordId = recordId, PreloadedCapabilities = null, AllowedTerminals = [new("assistant_proposal", 1)],
        AllowedCapabilities = [new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1), new("whatsapp.decline-form", 1),
            new("forms.suggestion-candidates", 1), new("forms.propose-suggestion", 1)]
    };

    public async Task<Result<string>> CaptureConfiguredCultureAsync(WhatsAppContact contact,CancellationToken ct)
    {
        var recipient=await recipients.FindAsync(contact.Id,ct);
        if(recipient?.UserId is not {} subject || subject!=contact.UserId || !recipient.IsActive || recipient.Culture is not ("es" or "en")
            || await recipientGuard.ValidateAsync(contact,subject,null,ct) is not null) return WhatsAppAssistantErrors.Unavailable;
        return recipient.Culture;
    }

    public async Task<Error?> RevalidateAsync(WhatsAppContact contact, WhatsAppAssistantReferenceRow reference, CancellationToken ct)
    {
        if (contact.Id != reference.ContactId || contact.UserId != reference.SubjectUserId || contact.LinkRevision != reference.LinkRevision
            || contact.ModeRevision != reference.ModeRevision || clock.GetUtcNow().UtcDateTime >= reference.ContentExpiresAtUtc)
            return WhatsAppAssistantErrors.StateConflict;
        if(reference.IsAlways && reference.Kind==WhatsAppAssistantRequestKind.Manual && reference.ActorUserId is null && reference.ContextJson is null)
        {
            var currentCulture=await CaptureConfiguredCultureAsync(contact,ct);
            return currentCulture.IsFailure?currentCulture.Error:currentCulture.Value==reference.CapturedCulture?null:WhatsAppAssistantErrors.StateConflict;
        }
        AssistantChannelContext? original;
        try { original = reference.ContextJson is {} json ? JsonSerializer.Deserialize<AssistantChannelContext>(json, JsonSerializerOptions.Web) : null; }
        catch (JsonException) { return WhatsAppAssistantErrors.StateConflict; }
        var captured = await CaptureAsync(contact, reference.Kind == WhatsAppAssistantRequestKind.Manual ? reference.ActorUserId : null, ct);
        if (captured.IsFailure) return captured.Error;
        return original is not null && original.ActorUserId == captured.Value.Context.ActorUserId
            && original.SubjectUserId == captured.Value.Context.SubjectUserId && original.ChannelId == captured.Value.Context.ChannelId
            && original.InteractionMode == captured.Value.Context.InteractionMode
            && (original.ChannelConversationId is null || original.ChannelConversationId == captured.Value.Context.ChannelConversationId)
            && original.Culture == captured.Value.Context.Culture ? null : WhatsAppAssistantErrors.StateConflict;
    }

}

internal sealed record WhatsAppAssistantContextResponse(AssistantChannelContext Context, string RecipientRevision);
