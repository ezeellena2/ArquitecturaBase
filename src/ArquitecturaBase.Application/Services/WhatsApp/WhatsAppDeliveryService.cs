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
internal sealed partial class WhatsAppDeliveryService(
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

    public async Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(waMessageId);
        LogHandling(logger, RecordSentOperation);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RecordSentCoreAsync(message, waMessageId, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogHandled(logger, RecordSentOperation);
        return result;
    }

    public async Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogHandling(logger, RecordUnsentOperation);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RecordUnsentCoreAsync(message, ct), CommitPolicy.OnSuccess, cancellationToken);

        LogHandled(logger, RecordUnsentOperation);
        return result;
    }

    // A propósito no toma whatsapp-message: ni la fila del contacto: los estados que llegan antes de que se confirme el
    // saliente se ignoran.
    private async Task<Result> RecordSentCoreAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        var contact = await FindContactAsync(message.To, cancellationToken);
        messages.Add(WhatsAppMessage.Outbound(
            contact?.Id,
            waMessageId,
            KindOf(message),
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);
}
