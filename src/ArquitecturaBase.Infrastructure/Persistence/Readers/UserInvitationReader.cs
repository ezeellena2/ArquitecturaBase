using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class UserInvitationReader(ApplicationDbContext dbContext) : IUserInvitationReader
{
    /// <summary>
    /// Va por el índice de la cuenta y la fecha, y desempata por el Id, que es un Guid v7 y crece con el tiempo. Las
    /// invitaciones no tienen borrado lógico: no hay filtro global.
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
                invitation.WaMessageId))
            .FirstOrDefaultAsync(cancellationToken);
}
