using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormSendPolicy(
    IWhatsAppFormRecipientReader recipients, IStructuredFormSource forms, IWhatsAppFlowSessionReader bindings,
    IWhatsAppFlowProtector flowProtector, IWhatsAppFlowEnvironment environment,
    IPermissionService permissions, IPhoneNumberParser phoneNumbers, TimeProvider timeProvider)
{
    public async Task<Result<WhatsAppFormSendContext>> ResolveAsync(SendWhatsAppFormRequest request, Guid actor,
        CancellationToken cancellationToken)
    {
        var recipient = await recipients.FindAsync(request.ContactId, cancellationToken);
        if (recipient is null) return WhatsAppChatErrors.NotFound;
        if (recipient.UserId is not { } userId || !recipient.IsActive || recipient.Culture is not ("es" or "en"))
            return FlowErrors.RecipientContextChanged;
        var recipientRevision = WhatsAppFormRecipientRevision.Create(actor, recipient);
        if (!string.Equals(recipientRevision, request.ExpectedRecipientContextRevision, StringComparison.Ordinal))
            return FlowErrors.RecipientContextChanged;
        if (!await HasSenderPermissionsAsync(actor, cancellationToken)) return WhatsAppChatErrors.NotFound;
        var form = await forms.FindAsync(userId, recipient.Culture, request.DefinitionId, request.VersionId, cancellationToken);
        if (form is null || !form.SubmitLabel.ContainsKey(recipient.Culture)) return FlowErrors.FormUnavailable;
        var buttonText = FlowTexts.Get("OpenForm", recipient.Culture);
        var binding = await bindings.FindBindingAsync(request.DefinitionId, request.VersionId, recipient.Culture, cancellationToken);
        if (binding is null) return FlowErrors.FormUnavailable;
        var projection = flowProtector.ReadProjection(binding.OperationId, binding.BindingId, binding.VersionId,
            binding.Culture, binding.ProtectedProjection);
        string? startScreen;
        try
        {
            using var document = JsonDocument.Parse(projection.Json);
            startScreen = document.RootElement.GetProperty("screens").EnumerateArray().FirstOrDefault().GetProperty("id").GetString();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return FlowErrors.FormUnavailable;
        }
        if (string.IsNullOrWhiteSpace(startScreen)) return FlowErrors.FormUnavailable;
        if (!environment.IsConfigured) return FlowErrors.AccountUnavailable;
        var account = await environment.VerifyAccountAsync(cancellationToken);
        if (account.IsFailure) return account.Error;
        var manifest = flowProtector.ReadManifest(binding.OperationId, binding.NeutralOperationId,
            binding.VersionId, binding.ProtectedManifest);
        if (!account.Value.HasSameIdentity(manifest.Account)) return FlowErrors.AccountMismatch;
        return new WhatsAppFormSendContext(recipient, recipientRevision, form, binding, projection, buttonText, startScreen, account.Value);
    }

    public async Task<Result<WhatsAppFormRecipientRow>> RevalidateAsync(WhatsAppFormSendContext context,
        SendWhatsAppFormRequest request, Guid actor, CancellationToken cancellationToken)
    {
        await forms.LockSelectionAsync(cancellationToken);
        var recipient = await recipients.FindAsync(request.ContactId, cancellationToken);
        if (recipient is null || recipient.UserId != context.Recipient.UserId || !recipient.IsActive
            || WhatsAppFormRecipientRevision.Create(actor, recipient) != context.RecipientRevision
            || !await HasSenderPermissionsAsync(actor, cancellationToken))
            return FlowErrors.RecipientContextChanged;
        var form = await forms.FindAsync(recipient.UserId!.Value, recipient.Culture!, request.DefinitionId, request.VersionId, cancellationToken);
        if (form is null) return FlowErrors.FormUnavailable;
        var binding = await bindings.FindBindingAsync(request.DefinitionId, request.VersionId, recipient.Culture!, cancellationToken);
        return binding?.BindingId == context.Binding.BindingId ? recipient : FlowErrors.FormUnavailable;
    }

    public Result<ArquitecturaBase.Domain.ValueObjects.PhoneNumber> ValidateContact(WhatsAppContact contact)
    {
        if (!WhatsAppChatService.IsReplyWindowOpen(contact, timeProvider.GetUtcNow().UtcDateTime))
            return WhatsAppChatErrors.WindowClosed;
        return WhatsAppIds.Parse(phoneNumbers, contact.WaId);
    }

    private async Task<bool> HasSenderPermissionsAsync(Guid actor, CancellationToken cancellationToken) =>
        await permissions.HasPermissionAsync(actor, WhatsAppPermissions.Read, cancellationToken)
        && await permissions.HasPermissionAsync(actor, WhatsAppPermissions.Send, cancellationToken);
}

internal sealed record WhatsAppFormSendContext(WhatsAppFormRecipientRow Recipient, string RecipientRevision,
    ArquitecturaBase.Application.Models.StructuredData.StructuredFormSelectionRow Form,
    WhatsAppFormBindingRow Binding, WhatsAppFlowProjection Projection, string ButtonText, string StartScreen,
    WhatsAppFlowAccountContext Account);
