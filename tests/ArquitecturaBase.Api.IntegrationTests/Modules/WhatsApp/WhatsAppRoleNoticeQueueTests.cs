using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppRoleNoticeQueueTests(ApiFactory factory)
{
    [Fact]
    public async Task A_manual_message_waits_behind_each_previously_queued_role_recipient_in_the_shared_FIFO()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, ct, clock);
        var runId = Guid.NewGuid();
        const int roleRecipients = 3;
        for (var index = 0; index < roleRecipients; index++)
        {
            var recipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, ct);
            var queued = await scenario.ScopeAsync(services => services.GetServices<IStructuredRecordActionChannel>()
                .Single(channel => channel.ChannelId == "phone").EnqueueAsync(new StructuredRecordActionMessage(runId,
                    recipient.UserId, "Aviso de rol " + index), ct));
            Assert.Equal(StructuredRecordActionState.Pending, queued.State);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        var manualRecipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, ct);
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send], ct);
        using var client = scenario.Host.CreateClient();
        using var accepted = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{manualRecipient.ContactId}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Atención manual" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        var dispatchMilliseconds = new List<double>();
        for (var index = 0; index <= roleRecipients; index++)
        {
            var elapsed = Stopwatch.StartNew();
            await worker.ProcessNextAsync(ct);
            dispatchMilliseconds.Add(elapsed.Elapsed.TotalMilliseconds);
            Assert.Equal(index + 1, scenario.Cloud.Messages.Count);
            var message = Assert.IsType<WhatsAppTextMessage>(scenario.Cloud.Messages[index]);
            Assert.Equal(index < roleRecipients ? "Aviso de rol " + index : "Atención manual", message.Body);
        }
        await worker.ProcessNextAsync(ct);
        Assert.Equal(roleRecipients + 1, scenario.Cloud.Messages.Count);
        await WhatsAppRoleQueueMeasurement.WriteAsync(roleRecipients, dispatchMilliseconds, ct);
    }
}
