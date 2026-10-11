using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormMessageDispatchService(
    IWhatsAppFlowSessionReader sessions, WhatsAppFlowSessionVerifier verifier,
    IWhatsAppFormRecipientReader recipients, IStructuredFormSource forms,
    IWhatsAppFlowEnvironment environment, IPermissionService permissions,
    IPhoneNumberParser phoneNumbers, ILogger<WhatsAppFormMessageDispatchService> logger) : IWhatsAppFormMessageDispatchService
{
    public Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(Guid outgoingId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppFlowMessageResponse>>(logger, "PrepareWhatsAppFlowMessage",
            () => PrepareCoreAsync(outgoingId, cancellationToken));

    private async Task<Result<WhatsAppFlowMessageResponse>> PrepareCoreAsync(Guid outgoingId, CancellationToken cancellationToken)
    {
        var row = await sessions.FindDispatchAsync(outgoingId, cancellationToken);
        if (row is null) return FlowErrors.StateConflict;
        var verified = verifier.Verify(row);
        if (verified.IsFailure) return verified.Error;
        var payload = verified.Value;

        var account = await environment.VerifyAccountAsync(cancellationToken);
        if (account.IsFailure) return account.Error;
        if (account.Value.WabaId != payload.Account.WabaId || account.Value.PhoneNumberId != payload.Account.PhoneNumberId
            || account.Value.GraphApiVersion != payload.Account.GraphApiVersion)
            return FlowErrors.AccountMismatch;
        if (!await permissions.HasPermissionAsync(row.OperatorUserId, WhatsAppPermissions.Read, cancellationToken)
            || !await permissions.HasPermissionAsync(row.OperatorUserId, WhatsAppPermissions.Send, cancellationToken))
            return WhatsAppChatErrors.SendPending;
        var recipient = await recipients.FindAsync(row.ContactId, cancellationToken);
        if (recipient?.UserId != row.UserId || !recipient.IsActive || recipient.Culture != row.Culture
            || WhatsAppFormRecipientRevision.Create(row.OperatorUserId, recipient) != payload.RecipientRevision)
            return FlowErrors.RecipientContextChanged;
        if (await forms.FindAcceptedSessionAsync(row.UserId, row.Culture, row.DefinitionId, row.VersionId, cancellationToken) is null)
            return FlowErrors.FormUnavailable;
        var phone = WhatsAppIds.Parse(phoneNumbers, row.WaId);
        return phone.IsFailure ? WhatsAppChatErrors.DestinationMissing
            : new WhatsAppFlowMessageResponse(phone.Value, row.Body!, payload.ButtonText, payload.MetaFlowId, payload.Token, payload.StartScreen, account.Value);
    }
}
