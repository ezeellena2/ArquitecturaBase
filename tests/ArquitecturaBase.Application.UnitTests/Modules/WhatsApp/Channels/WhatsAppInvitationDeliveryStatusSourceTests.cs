using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// El estado de entrega de una invitación por WhatsApp, el que avisa Meta del mensaje saliente: pendiente mientras no
/// tenga el id de Meta o Meta no haya avisado nada, y si no, el último estado registrado. Una rama por test, en el orden
/// en que las mira. Las fallidas antes de salir no llegan acá (las resuelve el detalle, LastInvitationTests).
/// </summary>
public sealed class WhatsAppInvitationDeliveryStatusSourceTests
{
    private const string WaMessageId = "wamid.HBgNNTQ5MzQxMzY1NDgxMxUCABEYEjBBQTQ1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeWhatsAppMessageReader _messages = new();

    [Fact]
    public void It_is_the_source_of_the_whatsapp_channel()
    {
        Assert.Equal(UserInvitationChannel.WhatsApp, Source().Channel);
    }

    [Fact]
    public async Task Without_a_meta_id_it_is_pending_without_reading_anything()
    {
        Assert.Equal(InvitationDeliveryStatus.Pending, await Source().FindStatusAsync(null, Ct));
        Assert.Empty(_messages.Reads);
    }

    [Fact]
    public async Task A_message_that_was_not_saved_is_pending()
    {
        Assert.Equal(InvitationDeliveryStatus.Pending, await Source().FindStatusAsync(WaMessageId, Ct));
        Assert.Equal([WaMessageId], _messages.Reads);
    }

    [Fact]
    public async Task A_message_without_a_status_yet_is_pending()
    {
        _messages.OutboundStatuses[WaMessageId] = null;

        Assert.Equal(InvitationDeliveryStatus.Pending, await Source().FindStatusAsync(WaMessageId, Ct));
    }

    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent, InvitationDeliveryStatus.Sent)]
    [InlineData(WhatsAppMessageStatus.Delivered, InvitationDeliveryStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read, InvitationDeliveryStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Failed, InvitationDeliveryStatus.Failed)]
    public async Task The_invitation_takes_the_status_of_its_message(
        WhatsAppMessageStatus status,
        InvitationDeliveryStatus expected)
    {
        _messages.OutboundStatuses[WaMessageId] = status;

        Assert.Equal(expected, await Source().FindStatusAsync(WaMessageId, Ct));
    }

    private WhatsAppInvitationDeliveryStatusSource Source() => new(_messages);
}
