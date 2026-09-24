using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Features.WhatsApp.RecordOutboundMessage;

/// <summary>
/// Guarda el saliente con el contacto del número, si existe: un código de ingreso puede ir a un número que nunca le
/// escribió al bot, y entonces queda sin contacto (sección 6.5 del spec del ingreso con WhatsApp). La hora es la
/// nuestra, la del envío. Si es una invitación, le deja el id de Meta: con él, el detalle del usuario lee el estado de
/// entrega que avisa el webhook (sección 12).
/// </summary>
internal sealed class RecordOutboundWhatsAppMessageCommandHandler(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IIdentityService identityService,
    IUserInvitationRepository invitations,
    TimeProvider timeProvider)
    : ICommandHandler<RecordOutboundWhatsAppMessageCommand>
{
    public async Task<Result> Handle(RecordOutboundWhatsAppMessageCommand command, CancellationToken cancellationToken)
    {
        var message = command.Message;
        var contact = await FindContactAsync(message.To, cancellationToken);

        messages.Add(WhatsAppMessage.Outbound(
            contact?.Id,
            command.WaMessageId,
            KindOf(message),
            message.SafeSummary,
            timeProvider.GetUtcNow().UtcDateTime));

        if (message is WhatsAppInvitationMessage sent)
        {
            await AttachToInvitationAsync(sent, command.WaMessageId, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// La cola encoló la invitación antes de que se confirmara la transacción que la guarda, y puede haberla mandado antes
    /// de que termine. Con el lock de la cuenta, que el alta y el reenvío tienen hasta confirmar, se la espera en lugar de
    /// no encontrarla. Si no está (esa transacción falló después de encolar), no hay a quién dejarle el id: el mensaje
    /// se guarda igual.
    /// </summary>
    private async Task AttachToInvitationAsync(WhatsAppInvitationMessage sent, string waMessageId, CancellationToken cancellationToken)
    {
        await invitations.LockAccountAsync(sent.UserId, cancellationToken);

        (await invitations.GetByIdAsync(sent.InvitationId, cancellationToken))?.AttachWhatsAppMessage(waMessageId);
    }

    /// <summary>
    /// Primero por el número, que WhatsApp manda como <c>wa_id</c> sin el "+" (y con el 9 de los celulares argentinos,
    /// como se guardan). Si no aparece así, por la cuenta que tiene ese número: su contacto vinculado es el de la persona.
    /// </summary>
    private async Task<WhatsAppContact?> FindContactAsync(PhoneNumber to, CancellationToken cancellationToken)
    {
        if (await contacts.GetLatestByWaIdAsync(to.Value[1..], cancellationToken) is { } byNumber)
        {
            return byNumber;
        }

        return await identityService.FindByPhoneAsync(to, cancellationToken) is { } account
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
