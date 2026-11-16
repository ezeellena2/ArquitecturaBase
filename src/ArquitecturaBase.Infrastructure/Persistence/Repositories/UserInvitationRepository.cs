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

    // Seguida, como toda entidad que devuelve un repositorio (Get…): se lee para decidir la espera y nadie la cambia,
    // así que el guardado final del límite no encuentra nada que bajar. Va por el índice de la cuenta y la fecha.
    // Desempata por el Id, que es un Guid v7 y crece con el tiempo.
    public Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserInvitations
            .Where(invitation => invitation.UserId == userId && !invitation.SendFailed)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .ThenByDescending(invitation => invitation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    // N7: sin índice propio; las invitaciones son pocas (las manda un administrador, de a una) y se cuentan una vez por
    // invitación o código por correo.
    public async Task<IReadOnlyList<DateTime>> ListLatestSentTimesAsync(
        UserInvitationChannel channel, DateTime sinceUtc, int count, CancellationToken cancellationToken) =>
        await dbContext.UserInvitations
            .Where(invitation => invitation.Channel == channel && invitation.SentAtUtc > sinceUtc)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .Select(invitation => invitation.SentAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTime>> ListLatestSentTimesBySenderAsync(
        UserInvitationChannel channel, Guid sentBy, DateTime sinceUtc, int count, CancellationToken cancellationToken) =>
        await dbContext.UserInvitations
            .Where(invitation => invitation.Channel == channel && invitation.SentBy == sentBy && invitation.SentAtUtc > sinceUtc)
            .OrderByDescending(invitation => invitation.SentAtUtc)
            .Select(invitation => invitation.SentAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

    public void Add(UserInvitation invitation) => dbContext.UserInvitations.Add(invitation);
}
