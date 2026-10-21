using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAudioPostgresDispatchTests(ApiFactory factory)
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_real_postgres_dispatch_copies_seals_replays_and_completes_audio(bool automatic)
    {
        var probe=new WhatsAppAudioDispatchProbe();
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct,builder=>builder.ConfigureTestServices(services=>
        {services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));}));
        var person=await host.ContactAsync(Ct,automatic);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        var messageId=await host.InTransactionAsync(async services=>
        {
            var db=services.GetRequiredService<ApplicationDbContext>();
            var contact=await db.Set<WhatsAppContact>().SingleAsync(row=>row.Id==person.ContactId,Ct);
            var message=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Audio,null,null,now);
            Assert.True(message.SetAudioDescriptor("12345","audio/ogg; codecs=opus",probe.Sha256));message.Classify(WhatsAppMessagePurpose.Business);
            message.SetInboundSequence(contact.RegisterInboundMessage());message.MarkProcessed(now);db.Add(message);return message.Id;
        },Ct);
        var first=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioDispatchService>().PrepareAsync(person.ContactId,Ct));
        Assert.True(first.IsSuccess);Assert.Equal(1,probe.Downloads);Assert.Equal(1,probe.Writes);
        if(!automatic)
        {
            Assert.Null(first.Value);
            var copied=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindAsync(person.ContactId,messageId,Ct));
            Assert.NotNull(copied);Assert.NotNull(copied.StorageKey);Assert.Null(copied.RequestId);Assert.Null(copied.ContextJson);
            Assert.Null(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(person.ContactId,now,Ct)));
            using var client=host.Client(host.FirstOperator);
            using var accepted=await client.PostAsync($"/api/whatsapp/chats/{person.ContactId}/messages/{messageId}/audio-transcription",null,Ct);
            Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
            first=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioDispatchService>().PrepareAsync(person.ContactId,Ct));
        }
        Assert.NotNull(first.Value);var work=first.Value;
        Assert.Equal(automatic?person.RecipientId:host.FirstOperator,work.Operation!.Context.ActorUserId);
        var reads=probe.Reads;
        var replay=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioDispatchService>().PrepareAsync(person.ContactId,Ct));
        Assert.True(replay.IsSuccess);Assert.NotNull(replay.Value);Assert.Equal(work.RequestId,replay.Value.RequestId);
        Assert.Equal(reads,probe.Reads);Assert.Equal(1,probe.Downloads);Assert.Equal(1,probe.Writes);
        var completed=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioDispatchService>().CompleteAsync(person.ContactId,work.RequestId,
            Result.Success(new AssistantOperationResponse(work.RequestId,"completed",null,null,null){NormalizedAudio=new("Visité el taller Sur.",["es"])}),Ct));
        Assert.True(completed.IsSuccess);
        var stored=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row=>row.Id==messageId,Ct));
        Assert.Equal(WhatsAppAudioState.Completed,stored.AudioState);Assert.Equal("Visité el taller Sur.",stored.Body);Assert.Equal(work.RequestId,stored.AudioRequestId);
        Assert.Null(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(person.ContactId,now,Ct)));
    }
    [Fact]
    public async Task Live_audio_keeps_polling_during_retry_backoff_without_becoming_dispatchable_early()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        var person=await host.ContactAsync(Ct);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        var messageId=await host.InTransactionAsync(services=>
        {
            var message=WhatsAppMessage.Inbound(person.ContactId,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Audio,null,null,now);
            Assert.True(message.SetAudioDescriptor("12345","audio/ogg",new string('a',64)));
            message.RegisterAudioFailure(now,"Files.Unavailable");
            services.GetRequiredService<ApplicationDbContext>().Add(message);
            return Task.FromResult(message.Id);
        },Ct);

        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().ExistsPendingAsync(now,Ct)));
        Assert.Null(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(person.ContactId,now,Ct)));
        host.Clock.Advance(TimeSpan.FromSeconds(29));
        var beforeDue=host.Clock.GetUtcNow().UtcDateTime;
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().ExistsPendingAsync(beforeDue,Ct)));
        Assert.Null(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(person.ContactId,beforeDue,Ct)));
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(person.ContactId,host.Clock.GetUtcNow().UtcDateTime,Ct)));

        await host.InTransactionAsync(async services=>
        {
            var message=await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(row=>row.Id==messageId,Ct);
            message.SetAudioState(WhatsAppAudioState.Unavailable,"Files.Unavailable");
            return true;
        },Ct);
        Assert.False(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().ExistsPendingAsync(host.Clock.GetUtcNow().UtcDateTime,Ct)));
    }}
