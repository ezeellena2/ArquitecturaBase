using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;

/// <summary>
/// La fuente del estado de entrega de un canal: responde <see cref="Status"/> y guarda qué ids le pidieron.
/// </summary>
internal sealed class FakeInvitationDeliveryStatusSource(UserInvitationChannel channel) : IInvitationDeliveryStatusSource
{
    public UserInvitationChannel Channel { get; } = channel;

    public InvitationDeliveryStatus Status { get; set; } = InvitationDeliveryStatus.Pending;

    public List<string?> Reads { get; } = [];

    public Task<InvitationDeliveryStatus> FindStatusAsync(string? providerMessageId, CancellationToken cancellationToken)
    {
        Reads.Add(providerMessageId);

        return Task.FromResult(Status);
    }
}
