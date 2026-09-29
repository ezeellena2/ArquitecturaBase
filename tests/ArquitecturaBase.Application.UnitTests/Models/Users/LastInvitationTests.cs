using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Models.Users;

/// <summary>
/// La traducción de la última invitación al estado de entrega que muestra el detalle: una rama por test, en el mismo
/// orden en que las mira (envío fallido, canal, id de Meta, estado del saliente).
/// </summary>
public sealed class LastInvitationTests
{
    private static readonly DateTime SentAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Email_invitation_that_went_out_has_no_delivery_status()
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.Email, SentAt, SendFailed: false, HasWaMessageId: false, OutboundStatus: null));

        Assert.Equal(new LastInvitation(UserInvitationChannel.Email, SentAt, DeliveryStatus: null), last);
    }

    // La cola de correo llena la deja SendFailed (P5 = B): sin el estado, el detalle la mostraría como enviada.
    [Fact]
    public void Email_invitation_that_could_not_be_sent_is_failed()
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.Email, SentAt, SendFailed: true, HasWaMessageId: false, OutboundStatus: null));

        Assert.Equal(new LastInvitation(UserInvitationChannel.Email, SentAt, InvitationDeliveryStatus.Failed), last);
    }

    [Fact]
    public void Whatsapp_invitation_that_could_not_be_sent_is_failed_whatever_its_message_says()
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.WhatsApp, SentAt, SendFailed: true, HasWaMessageId: true, WhatsAppMessageStatus.Read));

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, InvitationDeliveryStatus.Failed), last);
    }

    [Fact]
    public void Whatsapp_invitation_without_a_meta_id_is_pending()
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: false, OutboundStatus: null));

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, InvitationDeliveryStatus.Pending), last);
    }

    [Fact]
    public void Whatsapp_invitation_whose_message_has_no_status_yet_is_pending()
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: true, OutboundStatus: null));

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, InvitationDeliveryStatus.Pending), last);
    }

    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent, InvitationDeliveryStatus.Sent)]
    [InlineData(WhatsAppMessageStatus.Delivered, InvitationDeliveryStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read, InvitationDeliveryStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Failed, InvitationDeliveryStatus.Failed)]
    public void Whatsapp_invitation_takes_the_status_of_its_message(
        WhatsAppMessageStatus status,
        InvitationDeliveryStatus expected)
    {
        var last = LastInvitation.From(new UserInvitationRow(
            UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, HasWaMessageId: true, status));

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, expected), last);
    }
}
