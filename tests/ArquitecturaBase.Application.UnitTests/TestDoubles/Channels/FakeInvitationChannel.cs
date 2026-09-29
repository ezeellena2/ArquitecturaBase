using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;

/// <summary>
/// Un canal de invitación que acepta todo (o devuelve <see cref="Rejection"/>) y guarda lo que le pidieron: qué miró y
/// qué invitaciones encoló, con el idioma.
/// </summary>
internal sealed class FakeInvitationChannel(UserInvitationChannel channel, bool recordsConsent = false) : IInvitationChannel
{
    public UserInvitationChannel Channel { get; } = channel;

    public bool RecordsConsent { get; } = recordsConsent;

    public Error? Rejection { get; set; }

    public List<InvitationCheck> Checks { get; } = [];

    public List<(UserAccount User, UserInvitation Invitation, string Culture)> Enqueued { get; } = [];

    public Result Check(InvitationCheck check)
    {
        Checks.Add(check);

        return Rejection is { } error ? error : Result.Success();
    }

    public void Enqueue(UserAccount user, UserInvitation invitation, string culture) =>
        Enqueued.Add((user, invitation, culture));
}
