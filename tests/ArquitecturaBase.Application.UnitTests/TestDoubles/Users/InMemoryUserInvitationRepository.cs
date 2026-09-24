using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Users;

/// <summary>
/// Las invitaciones en memoria. Anota, en orden, cada lock de cuenta y cada lectura: así un test puede ver que el lock se
/// tomó antes de mirar.
/// </summary>
internal sealed class InMemoryUserInvitationRepository : IUserInvitationRepository
{
    public List<UserInvitation> Invitations { get; } = [];

    /// <summary>"lock:" y la cuenta, o "read:" y el método, en el orden en que pasaron.</summary>
    public List<string> Events { get; } = [];

    public Task LockAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        Events.Add("lock:" + userId);

        return Task.CompletedTask;
    }

    public Task<UserInvitation?> GetByIdAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        Events.Add("read:" + nameof(GetByIdAsync));

        return Task.FromResult(Invitations.SingleOrDefault(invitation => invitation.Id == invitationId));
    }

    public Task<UserInvitation?> GetLatestAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Invitations.Where(invitation => invitation.UserId == userId).MaxBy(invitation => invitation.SentAtUtc));

    public Task<UserInvitation?> GetLatestSentAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Invitations
            .Where(invitation => invitation.UserId == userId && !invitation.SendFailed)
            .MaxBy(invitation => invitation.SentAtUtc));

    public void Add(UserInvitation invitation) => Invitations.Add(invitation);
}
