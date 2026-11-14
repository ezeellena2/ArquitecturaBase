using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// H19: the mode keeps who changed it and when; a message and a send keep the trace context where they were written, the inbound
// round and the send continue it, and the call to Meta happens inside the trace of its send.
public sealed partial class WhatsAppChatTests
{
    [Fact]
    public async Task The_mode_keeps_who_changed_it_and_each_worker_continues_the_trace_of_its_row()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is WhatsAppTelemetry.SourceName or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var webhookTrace = ActivityTraceId.CreateRandom();
        var sendTrace = ActivityTraceId.CreateRandom();
        string? metaTrace = null;
        var providerId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ =>
        {
            metaTrace = Activity.Current?.TraceId.ToHexString();
            return FakeMetaHandler.Success(providerId);
        });
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        using var client = host.CreateClient();

        // The webhook's request is the trace of the message it keeps, and the round that processes it continues it.
        var phone = MetaWebhook.UniqueWaId();
        var bsuid = MetaWebhook.UniqueBsuid();
        var payload = MetaWebhook.Build(contacts: [MetaWebhook.Contact(phone, bsuid, "Cliente de la traza")],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), phone, bsuid, Now, "Hola")]);
        using (var webhook = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(payload) })
        {
            webhook.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(payload));
            webhook.Headers.Add("traceparent", TraceParent(webhookTrace));
            using var received = await client.SendAsync(webhook, Ct);
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        }
        var contactId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.WaId == phone)
            .Select(contact => contact.Id).SingleAsync(Ct));
        var inbound = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().Where(message => message.ContactId == contactId)
            .Select(message => message.TraceParent).SingleAsync(Ct));
        Assert.StartsWith($"00-{webhookTrace.ToHexString()}-", inbound, StringComparison.Ordinal);
        await host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        Assert.Contains(stopped, activity => activity.OperationName == "whatsapp.inbound" && activity.TraceId == webhookTrace);

        // Who takes the conversation back from the assistant, and when.
        await factory.InTransactionAsync(async services =>
        {
            var contact = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
            contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
            return true;
        });
        var actor = await OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send);
        using var mode = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/mode",
            JsonContent.Create(new { mode = "Manual", expectedRevision = 1 }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        // Both changes stay, each one added with its contact: the history is not loaded with it.
        var changes = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppModeChange>().Where(change => change.ContactId == contactId)
            .OrderBy(change => change.ModeRevision).Select(change => new { change.Mode, change.ChangedByUserId, change.ChangedAtUtc }).ToListAsync(Ct));
        Assert.Equal([WhatsAppConversationMode.Assistant, WhatsAppConversationMode.Manual], changes.Select(change => change.Mode));
        Assert.Equal([null, actor], changes.Select(change => change.ChangedByUserId));
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, changes[1].ChangedAtUtc, TimeSpan.FromMilliseconds(1));

        // The request of the operator is the trace of the send, which the worker continues with the call to Meta inside it.
        var requestId = Guid.NewGuid();
        using (var send = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/whatsapp/chats/{contactId}/messages", UriKind.Relative))
        {
            Content = JsonContent.Create(new { requestId, body = "Respuesta con traza" }),
        })
        {
            send.Headers.Add(TestAuthHandler.UserIdHeader, actor.ToString());
            send.Headers.Add("traceparent", TraceParent(sendTrace));
            using var sent = await client.SendAsync(send, Ct);
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        }
        var outgoing = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppOutgoingMessage>().Where(item => item.RequestId == requestId)
            .Select(item => item.TraceParent).SingleAsync(Ct));
        Assert.StartsWith($"00-{sendTrace.ToHexString()}-", outgoing, StringComparison.Ordinal);
        var worker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(1, meta.Calls);
        var dispatch = Assert.Single(stopped, activity => activity.OperationName == "whatsapp.outgoing" && activity.TraceId == sendTrace);
        Assert.Equal(outgoing!.Split('-')[2], dispatch.ParentSpanId.ToHexString());
        Assert.Equal(sendTrace.ToHexString(), metaTrace);
    }

    private static string TraceParent(ActivityTraceId trace) => $"00-{trace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01";
}
