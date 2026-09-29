using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Models.Users;

/// <summary>
/// La traducción de la última invitación al estado de entrega que muestra el detalle: la fallida gana, y si salió, el
/// estado que dio la fuente de su canal pasa tal cual (null si el canal no tiene fuente). Cómo traduce su estado la
/// fuente de WhatsApp lo prueba WhatsAppInvitationDeliveryStatusSourceTests.
/// </summary>
public sealed class LastInvitationTests
{
    private static readonly DateTime SentAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_invitation_without_a_tracked_status_has_no_delivery_status()
    {
        var last = LastInvitation.From(
            new UserInvitationRow(UserInvitationChannel.Email, SentAt, SendFailed: false, ProviderMessageId: null),
            trackedStatus: null);

        Assert.Equal(new LastInvitation(UserInvitationChannel.Email, SentAt, DeliveryStatus: null), last);
    }

    // La cola de correo llena la deja SendFailed (P5 = B): sin el estado, el detalle la mostraría como enviada.
    [Fact]
    public void Email_invitation_that_could_not_be_sent_is_failed()
    {
        var last = LastInvitation.From(
            new UserInvitationRow(UserInvitationChannel.Email, SentAt, SendFailed: true, ProviderMessageId: null),
            trackedStatus: null);

        Assert.Equal(new LastInvitation(UserInvitationChannel.Email, SentAt, InvitationDeliveryStatus.Failed), last);
    }

    [Fact]
    public void An_invitation_that_could_not_be_sent_is_failed_whatever_its_source_says()
    {
        var last = LastInvitation.From(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: true, "wamid.1"),
            InvitationDeliveryStatus.Read);

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, InvitationDeliveryStatus.Failed), last);
    }

    [Theory]
    [InlineData(InvitationDeliveryStatus.Pending)]
    [InlineData(InvitationDeliveryStatus.Sent)]
    [InlineData(InvitationDeliveryStatus.Delivered)]
    [InlineData(InvitationDeliveryStatus.Read)]
    [InlineData(InvitationDeliveryStatus.Failed)]
    public void An_invitation_that_went_out_takes_the_tracked_status_as_is(InvitationDeliveryStatus tracked)
    {
        var last = LastInvitation.From(
            new UserInvitationRow(UserInvitationChannel.WhatsApp, SentAt, SendFailed: false, "wamid.1"),
            tracked);

        Assert.Equal(new LastInvitation(UserInvitationChannel.WhatsApp, SentAt, tracked), last);
    }
}
