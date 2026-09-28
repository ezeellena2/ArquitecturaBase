using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Users;

/// <summary>La última invitación de cada cuenta, tal como la daría el lector. Anota cada cuenta que se leyó.</summary>
internal sealed class FakeUserInvitationReader : IUserInvitationReader
{
    public Dictionary<Guid, UserInvitationRow> Latest { get; } = [];

    public List<Guid> Reads { get; } = [];

    public Task<UserInvitationRow?> FindLatestAsync(Guid userId, CancellationToken cancellationToken)
    {
        Reads.Add(userId);

        return Task.FromResult(Latest.GetValueOrDefault(userId));
    }
}
