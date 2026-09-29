using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// El estado de entrega de un mensaje saliente guardado (IWhatsAppMessageReader) contra Postgres, el que lee la fuente
/// del estado de las invitaciones por WhatsApp: el último que avisó Meta, o null si no hay saliente con ese id o todavía
/// no avisó nada. El id de Meta es único entre entrantes y salientes, y un entrante no tiene estado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppMessageReaderTests(ApiFactory factory)
{
    private static readonly DateTime SentAt = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_message_that_was_not_saved_has_no_status()
    {
        Assert.Null(await FindOutboundStatusAsync(MetaWebhook.UniqueWaMessageId()));
    }

    [Fact]
    public async Task A_saved_message_without_a_status_yet_has_none()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        await AddAsync(Outbound(waMessageId));

        Assert.Null(await FindOutboundStatusAsync(waMessageId));
    }

    [Theory]
    [InlineData(WhatsAppMessageStatus.Sent)]
    [InlineData(WhatsAppMessageStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read)]
    [InlineData(WhatsAppMessageStatus.Failed)]
    public async Task An_outbound_message_has_the_last_status_meta_sent(WhatsAppMessageStatus status)
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var message = Outbound(waMessageId);
        message.ApplyStatus(status, SentAt.AddMinutes(1), status is WhatsAppMessageStatus.Failed ? 131026 : null);
        // Otro saliente con estado: la consulta no lo tiene que mirar.
        var other = Outbound(MetaWebhook.UniqueWaMessageId());
        other.ApplyStatus(WhatsAppMessageStatus.Delivered, SentAt.AddMinutes(1), errorCode: null);
        await AddAsync(message, other);

        Assert.Equal(status, await FindOutboundStatusAsync(waMessageId));
    }

    [Fact]
    public async Task An_inbound_message_is_not_an_outbound_one()
    {
        var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), userIdentifier: null, profileName: null, SentAt);
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        await factory.ExecuteDbContextAsync(db =>
        {
            db.Set<WhatsAppContact>().Add(contact);
            db.Set<WhatsAppMessage>().Add(
                WhatsAppMessage.Inbound(contact.Id, waMessageId, WhatsAppMessageKind.Text, "Hola", replyId: null, SentAt));
            return db.SaveChangesAsync(Ct);
        });

        Assert.Null(await FindOutboundStatusAsync(waMessageId));
    }

    private static WhatsAppMessage Outbound(string waMessageId) =>
        WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Template, "[invitación]", SentAt);

    private Task<int> AddAsync(params WhatsAppMessage[] messages) =>
        factory.ExecuteDbContextAsync(db =>
        {
            db.Set<WhatsAppMessage>().AddRange(messages);
            return db.SaveChangesAsync(Ct);
        });

    private Task<WhatsAppMessageStatus?> FindOutboundStatusAsync(string waMessageId) =>
        factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IWhatsAppMessageReader>().FindOutboundStatusAsync(waMessageId, Ct));
}
