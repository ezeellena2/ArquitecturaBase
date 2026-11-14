using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// H25: a row with a value this version does not know, as a newer one writes it while both run, is left for another copy: a
// message of a new kind reads as Other and the history of its chat opens; a send of a new kind waits instead of going out as text.
public sealed partial class WhatsAppChatTests
{
    [Fact]
    public async Task A_message_of_a_kind_this_version_does_not_know_keeps_the_history_of_its_chat()
    {
        var (contactId, _) = await ReceiveAsync();
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"WhatsAppMessages\" SET \"Kind\" = 'Hologram' WHERE \"ContactId\" = {contactId}", Ct));
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();

        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", userId: actor.ToString());

        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        Assert.Equal(WhatsAppMessageKind.Other, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>()
            .Where(message => message.ContactId == contactId).Select(message => message.Kind).SingleAsync(Ct)));
        Assert.Equal("Hologram", await factory.ExecuteDbContextAsync(db => db.Database.SqlQuery<string>(
            $"SELECT \"Kind\" AS \"Value\" FROM \"WhatsAppMessages\" WHERE \"ContactId\" = {contactId}").SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_send_of_a_kind_this_version_does_not_know_waits_instead_of_going_out_as_text()
    {
        var (contactId, _) = await ReceiveAsync();
        var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send);
        var meta = new FakeMetaHandler();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        using var client = host.CreateClient();
        await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        var requestId = Guid.NewGuid();
        using var sent = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contactId}/messages",
            JsonContent.Create(new { requestId, body = "Respuesta de otra versión" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"WhatsAppOutgoingMessages\" SET \"Kind\" = 99 WHERE \"RequestId\" = {requestId}", Ct));

        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.Equal(0, meta.Calls);
        var row = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppOutgoingMessage>().Where(item => item.RequestId == requestId)
            .Select(item => new { item.State, item.NextAttemptAtUtc, item.DispatchRetries }).SingleAsync(Ct));
        Assert.Equal((WhatsAppOutgoingMessageState.Pending, 0), (row.State, row.DispatchRetries));
        Assert.NotNull(row.NextAttemptAtUtc);
    }
}
