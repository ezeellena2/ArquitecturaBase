using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.WhatsApp.RecordUnsentMessage;

/// <summary>
/// Marca como fallida la invitación de un mensaje que no salió. Toma antes el lock de la cuenta, como cuando guarda uno
/// que salió: la cola puede llegar antes de que se confirme la transacción que guardó la invitación. Los demás mensajes
/// no dejan nada: el pedido que los encoló ya respondió, y la persona pide otro.
/// </summary>
internal sealed class RecordUnsentWhatsAppMessageCommandHandler(IUserInvitationRepository invitations)
    : ICommandHandler<RecordUnsentWhatsAppMessageCommand>
{
    public async Task<Result> Handle(RecordUnsentWhatsAppMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Message is WhatsAppInvitationMessage unsent)
        {
            await invitations.LockAccountAsync(unsent.UserId, cancellationToken);

            (await invitations.GetByIdAsync(unsent.InvitationId, cancellationToken))?.MarkSendFailed();
        }

        return Result.Success();
    }
}
