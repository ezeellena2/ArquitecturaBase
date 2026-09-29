using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// El emisor de invitaciones delega en el canal pedido: le pasa lo que mira con los campos del alta o del reenvío, y al
/// mandarla guarda la invitación con el consentimiento solo si el canal lo registra. Las reglas de cada canal las prueban
/// EmailInvitationChannelTests y WhatsAppInvitationChannelTests; el orden con el lock y la espera, los tests del servicio.
/// </summary>
public sealed class UserInvitationIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryUserInvitationRepository _invitations = new();
    private readonly InMemoryUserAccounts _accounts = new();
    private readonly FakeCurrentUser _admin = new() { UserId = Guid.CreateVersion7() };

    [Fact]
    public void The_channel_looks_at_the_invitation_with_the_fields_of_the_request()
    {
        var byEmail = new FakeInvitationChannel(UserInvitationChannel.Email);
        var byWhatsApp = new FakeInvitationChannel(UserInvitationChannel.WhatsApp, recordsConsent: true);

        var result = Issuer(byEmail, byWhatsApp).Check(
            UserInvitationChannel.WhatsApp, consent: true, "Ana", hasEmail: false, hasPhone: true, InvitationFields.OfCreate);

        Assert.True(result.IsSuccess);
        Assert.Empty(byEmail.Checks);
        Assert.Equal(
            [new InvitationCheck(false, true, true, "Ana", "invitation.channel", "invitation.consent", "displayName")],
            byWhatsApp.Checks);
    }

    [Fact]
    public void The_error_is_the_one_of_the_channel()
    {
        var byEmail = new FakeInvitationChannel(UserInvitationChannel.Email) { Rejection = UserInvitationErrors.UserInactive };

        var result = Issuer(byEmail).Check(
            UserInvitationChannel.Email, consent: false, null, hasEmail: true, hasPhone: false, InvitationFields.OfResend);

        Assert.Equal(UserInvitationErrors.UserInactive, result.Error);
        Assert.Equal("channel", Assert.Single(byEmail.Checks).ChannelField);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_invitation_records_the_consent_only_if_its_channel_does(bool recordsConsent)
    {
        var channel = new FakeInvitationChannel(UserInvitationChannel.WhatsApp, recordsConsent);
        var user = _accounts.AddUser(email: null, culture: "en", phoneNumber: "+5493515550101");

        await Issuer(channel).SendAsync(user, UserInvitationChannel.WhatsApp, Ct);

        var invitation = Assert.Single(_invitations.Invitations);
        Assert.Equal((user.Id, UserInvitationChannel.WhatsApp, _admin.UserId!.Value, Now.UtcDateTime),
            (invitation.UserId, invitation.Channel, invitation.SentBy, invitation.SentAtUtc));
        Assert.Equal(recordsConsent ? _admin.UserId : null, invitation.ConsentConfirmedBy);
        Assert.Equal(recordsConsent ? Now.UtcDateTime : null, invitation.ConsentConfirmedAtUtc);
        Assert.Equal([(user, invitation, "en")], channel.Enqueued);
        Assert.Equal(["lock:" + user.Id], _invitations.Events);
    }

    [Fact]
    public void Two_adapters_for_the_same_channel_are_a_registration_bug()
    {
        var issuer = Issuer(
            new FakeInvitationChannel(UserInvitationChannel.Email), new FakeInvitationChannel(UserInvitationChannel.Email));

        Assert.Throws<InvalidOperationException>(() => issuer.Check(
            UserInvitationChannel.Email, consent: false, null, hasEmail: true, hasPhone: false, InvitationFields.OfResend));
    }

    private UserInvitationIssuer Issuer(params IInvitationChannel[] channels) =>
        new(_invitations, channels, _admin, new FakeTimeProvider(Now));
}
