using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class StructuredFormAppPreviewDriver : IStructuredFormAppPreviewDriver
{
    public async Task<StructuredFormAppPreviewContext> PrepareAsync(ApiFactory factory, Action<IWebHostBuilder> configure,
        IReadOnlyList<string> managerPermissions, byte[] audio, CancellationToken cancellationToken)
    {
        var preview = WhatsAppFormAppPreview.FromStageFiveEnvironment();
        Assert.NotNull(preview);
        var scenario = await WhatsAppAssistantScenario.CreatePreviewAsync(factory, builder =>
        {
            preview.Configure(builder);
            builder.ConfigureTestServices(WhatsAppAssistantCaptureFixture.Configure);
            configure(builder);
        }, [.. managerPermissions, WhatsAppPermissions.ConfigureAssistant], cancellationToken);
        try
        {
            scenario.Clock.AdjustTime(TimeProvider.System.GetUtcNow());
            var contact = await scenario.ContactAsync(cancellationToken);
            using var client = scenario.Client(scenario.FirstOperator);
            var requestId = Guid.NewGuid();
            using var requested = await client.PostAsJsonAsync($"/api/whatsapp/chats/{contact.ContactId}/assistant-suggestions",
                new { requestId, sourceMessageId = contact.SourceMessageId, expectedModeRevision = 0 }, cancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
            await scenario.CompleteAsync(contact, requestId, "ready", cancellationToken);

            var audioContact = await scenario.ContactAsync(cancellationToken);
            var audioSeeds = await scenario.InTransactionAsync(async services =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                var now = scenario.Clock.GetUtcNow().UtcDateTime;
                var row = await db.Set<WhatsAppContact>().SingleAsync(value => value.Id == audioContact.ContactId, cancellationToken);
                row.RecordInbound(null, null, "Audio ficticio", now);
                var message = WhatsAppMessage.Inbound(row.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Audio, null, null, now);
                Assert.True(message.SetAudioDescriptor("12345", "audio/ogg; codecs=opus", Convert.ToHexStringLower(SHA256.HashData(audio))));
                message.Classify(WhatsAppMessagePurpose.Business);
                message.SetInboundSequence(row.RegisterInboundMessage());
                message.MarkProcessed(now);
                var key = $"whatsapp/audio/{message.Id:N}.ogg";
                Assert.True(message.StoreAudio(key, audio.Length).IsSuccess);
                var normalization = message.RequestAudioNormalization(scenario.ReadOnlyOperator, "{}", row.LinkRevision, now.AddHours(1), now);
                Assert.True(normalization.IsSuccess);
                Assert.True(message.CompleteAudio(normalization.Value, "Visité el local y tomé una foto.", now).IsSuccess);
                var pending = WhatsAppMessage.Inbound(row.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Audio, null, null, now);
                Assert.True(pending.SetAudioDescriptor("12346", "audio/ogg; codecs=opus", Convert.ToHexStringLower(SHA256.HashData(audio))));
                pending.Classify(WhatsAppMessagePurpose.Business);
                pending.SetInboundSequence(row.RegisterInboundMessage());
                pending.MarkProcessed(now);
                var pendingKey = $"whatsapp/audio/{pending.Id:N}.ogg";
                Assert.True(pending.StoreAudio(pendingKey, audio.Length).IsSuccess);
                Assert.Equal(WhatsAppAudioState.Pending, pending.AudioState);
                Assert.Null(pending.AudioRequestId);
                db.Set<WhatsAppMessage>().AddRange(message, pending);
                return (CompletedKey: key, PendingKey: pendingKey, PendingMessageId: pending.Id);
            }, cancellationToken);
            await scenario.ScopeAsync(async services =>
            {
                foreach (var key in new[] { audioSeeds.CompletedKey, audioSeeds.PendingKey })
                {
                    await using var stream = new MemoryStream(audio, false);
                    Assert.True(await services.GetRequiredService<IPrivateFileStore>().WriteOnceAsync(key, stream, "audio/ogg", cancellationToken));
                }
            });

            scenario.Forms.SuggestionMode = "Always";
            scenario.Operations.AvailabilityState = "notInstalled";
            var alwaysContact = await scenario.ContactAsync(cancellationToken);
            var parent = await WhatsAppAssistantCaptureFixture.SendManualParentAsync(scenario, alwaysContact, cancellationToken);
            var captured = await WhatsAppAssistantCaptureFixture.ReplyAsync(scenario, alwaysContact, parent, scenario.Forms.FieldId, cancellationToken);
            await scenario.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(cancellationToken);
            using var alwaysThread = await client.GetAsync($"/api/whatsapp/chats/{alwaysContact.ContactId}/messages", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, alwaysThread.StatusCode);
            var thread = await alwaysThread.ReadJsonAsync();
            Assert.Equal("notInstalled", thread.GetProperty("assistantAvailability").GetProperty("state").GetString());
            var pointer = thread.GetProperty("assistantSuggestion");
            Assert.Equal(captured.MessageId, pointer.GetProperty("sourceMessageId").GetGuid());
            var alwaysRequest = pointer.GetProperty("requestId").GetGuid();
            using var alwaysReady = await client.GetAsync($"/api/whatsapp/chats/{alwaysContact.ContactId}/assistant-suggestions/{alwaysRequest}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, alwaysReady.StatusCode);
            var ready = await alwaysReady.ReadJsonAsync();
            Assert.Equal("Ready", ready.GetProperty("state").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, ready.GetProperty("proposal").GetProperty("proposalId").ValueKind);
            var assistantContact = await scenario.ContactAsync(cancellationToken, automatic: true);
            scenario.Operations.AvailabilityState = "available";

            async Task HoldAsync(IReadOnlyList<(string Key, Guid ActorId)> actors, object fixtures, Func<CancellationToken, Task> advance, CancellationToken ct) =>
                await preview.HoldAsync(factory, scenario.Host,
                    [("manager", scenario.FirstOperator, contact.ContactId, "es"),
                     ("reader", scenario.ReadOnlyOperator, audioContact.ContactId, "es"),
                     ("restricted", scenario.RestrictedOperator, contact.ContactId, "es")],
                    scenario.Clock, new ControlledWhatsAppSendClient(), ct, fixtures, advance, actors,
                    state => scenario.Operations.AvailabilityState = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(state.ToString()));
            return new(scenario.Host, scenario.FirstOperator, contact.RecipientId, scenario.ReadOnlyOperator,
                scenario.RestrictedOperator, contact.ContactId, audioContact.ContactId, audioSeeds.PendingMessageId, alwaysContact.ContactId, assistantContact.ContactId, scenario, HoldAsync);
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
    }
}
