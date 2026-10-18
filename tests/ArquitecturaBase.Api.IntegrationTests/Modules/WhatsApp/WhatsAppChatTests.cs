using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed partial class WhatsAppChatTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Reading_is_per_operator_and_replayed_webhooks_do_not_increment_unread()
    {
        var (contactId, payload) = await ReceiveAsync();
        var first = await OperatorAsync(WhatsAppPermissions.Read);
        var second = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();
        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", userId: first.ToString());
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        Assert.Equal(1, (await thread.ReadJsonAsync()).GetProperty("contact").GetProperty("unreadCount").GetInt32());
        using var read = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/read",
            JsonContent.Create(new { throughSequence = 1 }), userId: first.ToString());
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        await PostWebhookAsync(payload);
        using var after = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", userId: first.ToString());
        using var other = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", userId: second.ToString());
        Assert.Equal(0, (await after.ReadJsonAsync()).GetProperty("contact").GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, (await other.ReadJsonAsync()).GetProperty("contact").GetProperty("unreadCount").GetInt32());
        using var list = await client.SendAsync(HttpMethod.Get, "/api/whatsapp/chats?unreadOnly=true&pageSize=10", userId: second.ToString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True((await list.ReadJsonAsync()).GetProperty("unreadCount").GetInt32() > 0);
    }

    [Fact]
    public async Task Manual_reply_is_durable_idempotent_and_applies_a_receipt_that_arrived_before_completion()
    {
        var (contactId, _) = await ReceiveAsync();
        var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.ConfigureAssistant);
        using var client = factory.CreateClient();
        using var mode = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/mode",
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        var requestId = Guid.NewGuid();
        var route = $"/api/whatsapp/chats/{contactId}/messages";
        using var sent = await client.SendAsync(HttpMethod.Post, route, JsonContent.Create(new { requestId, body = "Respuesta del operador" }), userId: actor.ToString());
        using var repeated = await client.SendAsync(HttpMethod.Post, route, JsonContent.Create(new { requestId, body = "Respuesta del operador" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        var operationId = (await sent.ReadJsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal(operationId, (await repeated.ReadJsonAsync()).GetProperty("id").GetGuid());
        var reserved = await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IWhatsAppChatDispatchService>().ReserveAsync(Ct)).Value);
        Assert.NotNull(reserved);
        Assert.Equal(operationId, reserved.Id);
        var waId = MetaWebhook.UniqueWaMessageId();
        await PostWebhookAsync(MetaWebhook.Build(statuses: [MetaWebhook.Status(waId, "delivered", Now.AddSeconds(1))]));
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppChatDispatchService>()
            .CompleteAsync(contactId, operationId, waId, uncertain: false, Ct));
        using var history = await client.SendAsync(HttpMethod.Get, route, userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var messages = (await history.ReadJsonAsync()).GetProperty("messages").EnumerateArray().ToArray();
        var outbound = Assert.Single(messages, item => item.GetProperty("direction").GetString() == "Outbound");
        Assert.Equal("Delivered", outbound.GetProperty("status").GetString());
        Assert.Equal(actor, outbound.GetProperty("operatorUserId").GetGuid());
    }

    [Fact]
    public async Task Permissions_and_mode_conflicts_are_enforced_by_the_api()
    {
        var (contactId, _) = await ReceiveAsync();
        var reader = await OperatorAsync(WhatsAppPermissions.Read);
        var sender = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send);
        var senderOnly = await OperatorAsync(WhatsAppPermissions.Send);
        var configureOnly = await OperatorAsync(WhatsAppPermissions.ConfigureAssistant);
        var legacyManager = await OperatorAsync("whatsapp.manage");
        using var client = factory.CreateClient();
        var route = $"/api/whatsapp/chats/{contactId}/messages";
        var modeRoute = $"/api/whatsapp/chats/{contactId}/mode";
        using var anonymous = await client.SendAsync(HttpMethod.Put, modeRoute, JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var denied = await client.SendAsync(HttpMethod.Post, route,
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Hola" }), userId: reader.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var cannotStop = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: reader.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, cannotStop.StatusCode);
        using var retiredPermission = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: legacyManager.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, retiredPermission.StatusCode);
        using var cannotEnable = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Assistant", expectedRevision = 0 }), userId: senderOnly.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, cannotEnable.StatusCode);
        using var retiredMode = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Bot", expectedRevision = 0 }), userId: configureOnly.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, retiredMode.StatusCode);
        await factory.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
            (await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct)).ChangeMode(WhatsAppConversationMode.Assistant);
        });
        using var assistant = await client.SendAsync(HttpMethod.Post, route,
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Hola" }), userId: sender.ToString());
        Assert.Equal(HttpStatusCode.Conflict, assistant.StatusCode);
        Assert.Equal(WhatsAppChatErrors.ManualRequiredCode, (await assistant.ReadJsonAsync()).GetProperty("code").GetString());
        using var stale = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: senderOnly.ToString());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var taken = await client.SendAsync(HttpMethod.Put, modeRoute,
            JsonContent.Create(new { mode = "Manual", expectedRevision = 1 }), userId: senderOnly.ToString());
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        var changed = await taken.ReadJsonAsync();
        Assert.Equal("Manual", changed.GetProperty("mode").GetString());
        Assert.Equal(2, changed.GetProperty("modeRevision").GetInt64());
        Assert.False(changed.GetProperty("assistantSendInProgress").GetBoolean());
    }

    [Fact]
    public async Task Expired_sending_becomes_uncertain_and_requires_its_operator_to_review_it()
    {
        var (contactId, _) = await ReceiveAsync();
        var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.ConfigureAssistant);
        using var client = factory.CreateClient();
        using var mode = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/mode",
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        var modeReceipt = await mode.ReadJsonAsync();
        Assert.Equal("Manual", modeReceipt.GetProperty("mode").GetString());
        Assert.Equal(0, modeReceipt.GetProperty("modeRevision").GetInt64());
        Assert.False(modeReceipt.GetProperty("assistantSendInProgress").GetBoolean());
        var requestId = Guid.NewGuid();
        using var sent = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contactId}/messages",
            JsonContent.Create(new { requestId, body = "Respuesta sin confirmar" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var id = (await sent.ReadJsonAsync()).GetProperty("id").GetGuid();
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppChatDispatchService>().ReserveAsync(Ct));
        await factory.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<Infrastructure.Persistence.ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "WhatsAppOutgoingMessages" SET "StartedAtUtc" = {Now.AddMinutes(-3)} WHERE "Id" = {id}""", Ct);
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppChatDispatchService>().RecoverExpiredAsync(Ct));
        using var outcome = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/outgoing/{requestId}", userId: actor.ToString());
        Assert.Equal("Uncertain", (await outcome.ReadJsonAsync()).GetProperty("state").GetString());
        using var review = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contactId}/outgoing/{requestId}/review", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.NoContent, review.StatusCode);
        var reserved = await factory.ExecuteScopeAsync(async services => (await services.GetRequiredService<IWhatsAppChatDispatchService>().ReserveAsync(Ct)).Value);
        Assert.Null(reserved);
    }

    [Fact]
    public async Task Business_processing_does_not_reply_and_the_worker_sends_the_operator_reply_once()
    {
        var (contactId, _) = await ReceiveAsync();
        var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.ConfigureAssistant);
        var providerId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(providerId));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        using var client = host.CreateClient();
        using var mode = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/mode",
            JsonContent.Create(new { mode = "Manual", expectedRevision = 0 }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        var modeReceipt = await mode.ReadJsonAsync();
        Assert.Equal("Manual", modeReceipt.GetProperty("mode").GetString());
        Assert.Equal(0, modeReceipt.GetProperty("modeRevision").GetInt64());
        Assert.False(modeReceipt.GetProperty("assistantSendInProgress").GetBoolean());
        await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        var processed = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().Where(message => message.ContactId == contactId).SingleAsync(Ct));
        Assert.NotNull(processed.ProcessedAtUtc);
        var requestId = Guid.NewGuid();
        using var sent = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contactId}/messages",
            JsonContent.Create(new { requestId, body = "Respuesta para el cliente" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var worker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(1, meta.Calls);
        using var outcome = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/outgoing/{requestId}", userId: actor.ToString());
        Assert.Equal("Accepted", (await outcome.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().CountAsync(message => message.WaMessageId == providerId, Ct)));
    }

    [Fact]
    public async Task History_cursor_keeps_messages_with_identical_timestamps_without_overlap()
    {
        var (contactId, _) = await ReceiveAsync();
        var contact = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct));
        await PostWebhookAsync(MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: Enumerable.Range(0, 4).Select(index => MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), contact.WaId, contact.UserIdentifier, Now, "Mensaje " + index))));
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();
        var route = $"/api/whatsapp/chats/{contactId}/messages?limit=2";
        using var first = await client.SendAsync(HttpMethod.Get, route, userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var page = await first.ReadJsonAsync();
        var cursor = Uri.EscapeDataString(page.GetProperty("beforeAtUtc").GetString()!);
        var id = page.GetProperty("beforeId").GetGuid();
        using var second = await client.SendAsync(HttpMethod.Get, route + $"&beforeAtUtc={cursor}&beforeId={id}", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var older = await second.ReadJsonAsync();
        var recentIds = page.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("id").GetGuid()).ToArray();
        var olderIds = older.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(2, olderIds.Length);
        Assert.Empty(recentIds.Intersect(olderIds));
    }

    private async Task<(Guid ContactId, byte[] Payload)> ReceiveAsync()
    {
        var phone = MetaWebhook.UniqueWaId();
        var bsuid = MetaWebhook.UniqueBsuid();
        var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(phone, bsuid, "Cliente del chat")],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), phone, bsuid, Now, "Hola, necesito ayuda")]);
        await PostWebhookAsync(payload);
        var contactId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.WaId == phone).Select(contact => contact.Id).SingleAsync(Ct));
        return (contactId, payload);
    }

    private async Task PostWebhookAsync(byte[] body)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<Guid> OperatorAsync(params string[] permissions) => factory.InTransactionAsync(async services =>
    {
        var roleName = "chat-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, Ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
        await users.SetRolesAsync(user.Id, [roleName], Ct);
        return user.Id;
    });
}
