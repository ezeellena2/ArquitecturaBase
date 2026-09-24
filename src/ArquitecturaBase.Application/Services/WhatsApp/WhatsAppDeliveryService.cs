using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

internal sealed partial class WhatsAppDeliveryService(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IUserReader users,
    IUserInvitationRepository invitations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<WhatsAppDeliveryService> logger) : IWhatsAppDeliveryService
{
    public async Task<Result> RecordSentAsync(
        WhatsAppOutboundMessage message, string waMessageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrEmpty(waMessageId);
        LogHandling(logger, "RecordOutboundWhatsAppMessageCommand");

        var contact = await FindContactAsync(message.To, cancellationToken);
        messages.Add(WhatsAppMessage.Outbound(
            contact?.Id,
            waMessageId,
            KindOf(message),
            message.SafeSummary,
            timeProvider.GetUtcNow().UtcDateTime));

        if (message is WhatsAppInvitationMessage invitation)
        {
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.AttachWhatsAppMessage(waMessageId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogHandled(logger, "RecordOutboundWhatsAppMessageCommand");
        return Result.Success();
    }

    public async Task<Result> RecordUnsentAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogHandling(logger, "RecordUnsentWhatsAppMessageCommand");

        if (message is WhatsAppInvitationMessage invitation)
        {
            await invitations.LockAccountAsync(invitation.UserId, cancellationToken);
            (await invitations.GetByIdAsync(invitation.InvitationId, cancellationToken))?.MarkSendFailed();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogHandled(logger, "RecordUnsentWhatsAppMessageCommand");
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
