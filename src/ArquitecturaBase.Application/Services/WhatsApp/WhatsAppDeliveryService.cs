using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// Registra en el historial lo que la cola de WhatsApp mandó o no pudo mandar. Cada registro corre en su propio límite
/// (ExecuteInTransactionAsync con OnSuccess), en el scope que abre WhatsAppSenderBackgroundService después del HTTP a
/// Meta, que va fuera de toda transacción.
/// </summary>
internal sealed class WhatsAppDeliveryService(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IUserReader users,
    IUserInvitationRepository invitations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<WhatsAppDeliveryService> logger) : IWhatsAppDeliveryService
{
    private const string RecordSentOperation = "RecordSentWhatsAppMessage";
    private const string RecordUnsentOperation = "RecordUnsentWhatsAppMessage";

    public Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(waMessageId);

        // Un tipo de mensaje desconocido es un error de programación: lanza antes de abrir el límite.
        var kind = KindOf(message);

        return OperationLog.RunAsync<Result>(logger, RecordSentOperation, () =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => RecordSentCoreAsync(message, kind, waMessageId, ct), CommitPolicy.OnSuccess, cancellationToken));
    }

    public Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return OperationLog.RunAsync<Result>(logger, RecordUnsentOperation, () =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => RecordUnsentCoreAsync(message, ct), CommitPolicy.OnSuccess, cancellationToken));
    }

    // A propósito no toma el lock "whatsapp-message:" ni la fila del contacto: los estados que llegan antes de que se
    // confirme el saliente se ignoran.
    private async Task<Result> RecordSentCoreAsync(
        WhatsAppOutboundMessage message,
        WhatsAppMessageKind kind,
        string waMessageId,
        CancellationToken cancellationToken)
    {
        var contact = await FindContactAsync(message.To, cancellationToken);
        messages.Add(WhatsAppMessage.Outbound(
            contact?.Id,
            waMessageId,
            kind,
            message.SafeSummary,
            timeProvider.GetUtcNow().UtcDateTime));

        if (message is WhatsAppInvitationMessage invitation)
        {
            // El lock espera el commit de quien encoló la invitación: la encuentra aunque Meta haya contestado antes.
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.AttachWhatsAppMessage(waMessageId);
        }

        return Result.Success();
    }

    // Con un mensaje que no es una invitación no hay nada que marcar, y el límite se abre y se confirma vacío. Es a
    // propósito: un mensaje sin mandar es raro, y así los dos registros siguen el mismo camino.
    private async Task<Result> RecordUnsentCoreAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        if (message is WhatsAppInvitationMessage invitation)
        {
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.MarkSendFailed();
        }

        return Result.Success();
    }

    private async Task<WhatsAppContact?> FindContactAsync(PhoneNumber to, CancellationToken cancellationToken)
    {
        if (await contacts.GetLatestByWaIdAsync(to.Value[1..], cancellationToken) is { } byNumber)
        {
            return byNumber;
        }

        return await users.FindByPhoneAsync(to, cancellationToken) is { } account
            ? await contacts.GetByUserIdAsync(account.Id, cancellationToken)
            : null;
    }

    private static WhatsAppMessageKind KindOf(WhatsAppOutboundMessage message) => message switch
    {
        WhatsAppTextMessage => WhatsAppMessageKind.Text,
        WhatsAppLinkButtonMessage or WhatsAppReplyButtonsMessage => WhatsAppMessageKind.Interactive,
        WhatsAppLoginCodeMessage or WhatsAppInvitationMessage => WhatsAppMessageKind.Template,
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Unknown WhatsApp message type."),
    };

}
