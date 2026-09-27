using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class UserInvitationRepository(ApplicationDbContext dbContext) : IUserInvitationRepository
{
    public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([AdvisoryLockKeys.UserInvitation(userId)], cancellationToken);

    public Task<UserInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations.SingleOrDefaultAsync(invitation => invitation.Id == invitationId, cancellationToken);

    // Seguidas, como toda entidad que devuelve un repositorio (Get…): se leen para mostrar o para decidir y nadie las
    // cambia, así que el guardado final del límite no encuentra nada que bajar. Van por el índice de la cuenta y la
    // fecha. Desempatan por el Id, que es un Guid v7 y crece con el tiempo.
    public Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .Where(invitation => invitation.UserId == userId)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .Where(invitation => invitation.UserId == userId && !invitation.SendFailed)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(UserInvitation invitation) => dbContext.UserInvitations.Add(invitation);
}
