using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.RecordUnsentMessage;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Features.WhatsApp;

/// <summary>
/// Un mensaje que la cola no pudo mandar: Meta lo rechazó (por ejemplo, la plantilla todavía no está aprobada) o se
/// agotaron los reintentos. Para una invitación, el admin tiene que poder verlo: si no, quedaría pendiente para siempre.
/// </summary>
public sealed class RecordUnsentWhatsAppMessageTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+5493413654813").Value;
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserInvitationRepository _invitations = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invitation_that_was_not_sent_is_marked_as_failed_after_taking_the_lock_of_its_account()
    {
        var userId = Guid.CreateVersion7();
        var invitation = UserInvitation.ByWhatsApp(userId, Guid.CreateVersion7(), Now);
        _invitations.Invitations.Add(invitation);

        var result = await RecordAsync(
            new WhatsAppInvitationMessage(Phone, userId, invitation.Id, "es", "Laura Ríos", "Arquitectura Base", "WANT_TO_ENTER"));

        Assert.True(result.IsSuccess);
        Assert.True(invitation.SendFailed);
        Assert.Equal(["lock:" + userId, "read:GetByIdAsync"], _invitations.Events);
    }

    [Fact]
    public async Task Other_messages_that_were_not_sent_change_nothing()
    {
        var result = await RecordAsync(new WhatsAppLoginCodeMessage(Phone, "es", "482913"));

        Assert.True(result.IsSuccess);
        Assert.Empty(_invitations.Events);
    }

    private Task<Domain.Results.Result> RecordAsync(WhatsAppOutboundMessage message) =>
        new RecordUnsentWhatsAppMessageCommandHandler(_invitations).Handle(new RecordUnsentWhatsAppMessageCommand(message), Ct);
}
