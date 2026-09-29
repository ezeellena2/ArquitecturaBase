using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class UserInvitationReader(ApplicationDbContext dbContext) : IUserInvitationReader
{
    /// <summary>
    /// Una sola consulta: va por el índice de la cuenta y la fecha, y desempata por el Id, que es un Guid v7 y crece con el
    /// tiempo. El estado del saliente es una subconsulta por el índice único del id de Meta; sin id, o sin mensaje
    /// guardado con ese id, queda en null. Ni las invitaciones ni los mensajes tienen borrado lógico: no hay filtro global.
    /// </summary>
    public Task<UserInvitationRow?> FindLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .Select(invitation => new UserInvitationRow(
                invitation.Channel,
                invitation.SentAtUtc,
                invitation.SendFailed,
                invitation.WaMessageId != null,
                dbContext.Set<WhatsAppMessage>()
                    .Where(message => invitation.WaMessageId != null
                        && message.Direction == WhatsAppMessageDirection.Outbound
                        && message.WaMessageId == invitation.WaMessageId)
                    .Select(message => message.Status)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
}
