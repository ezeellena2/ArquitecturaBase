using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class UserInvitationRepository(ApplicationDbContext dbContext) : IUserInvitationRepository
{
    private const string LockKeyPrefix = "user-invitation:";

    public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([LockKeyPrefix + userId.ToString("N")], cancellationToken);

    public Task<UserInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations.SingleOrDefaultAsync(invitation => invitation.Id == invitationId, cancellationToken);

    // Sin seguimiento: las dos se leen para mostrar o para decidir, no para cambiarlas. Van por el índice de la cuenta y
    // la fecha. Desempatan por el Id, que es un Guid v7 y crece con el tiempo.
    public Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId && !invitation.SendFailed)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(UserInvitation invitation) => dbContext.UserInvitations.Add(invitation);
}
