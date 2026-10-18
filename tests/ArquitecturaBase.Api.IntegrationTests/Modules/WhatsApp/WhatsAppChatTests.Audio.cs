using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed partial class WhatsAppChatTests
{
    private static byte[] AudioBytes()
    {
        var bytes=new byte[47];"OggS"u8.CopyTo(bytes);bytes[26]=1;bytes[27]=19;"OpusHead"u8.CopyTo(bytes.AsSpan(28));bytes[36]=1;bytes[37]=1;return bytes;
    }
    private Task<(Guid Contact,Guid Message,string Key)> AddAudioAsync(DateTime occurred,bool stored=false)=>factory.InTransactionAsync(async services=>
    {
        var db=services.GetRequiredService<ApplicationDbContext>();var contact=WhatsAppContact.Create(MetaWebhook.UniqueWaId(),null,"Audio sintético",occurred);
        var message=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Audio,null,null,occurred);
        message.SetAudioDescriptor("12345","audio/ogg; codecs=opus",Convert.ToHexStringLower(SHA256.HashData(AudioBytes())));message.Classify(WhatsAppMessagePurpose.Business);message.SetInboundSequence(1);
        var key=$"whatsapp/audio/{message.Id:N}.ogg";if(stored)message.StoreAudio(key,AudioBytes().Length);
        db.AddRange(contact,message);await Task.CompletedTask;return(contact.Id,message.Id,key);
    });
    [Fact]
    public async Task Audio_request_is_idempotent_for_two_read_only_operators_without_send_or_home()
    {
        var row=await AddAudioAsync(Now);var first=await OperatorAsync(WhatsAppPermissions.Read);var second=await OperatorAsync(WhatsAppPermissions.Read);
        using var client=factory.CreateClient();var route=$"/api/whatsapp/chats/{row.Contact}/messages/{row.Message}/audio-transcription";
        using var accepted=await client.SendAsync(HttpMethod.Post,route,userId:first.ToString());Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        await WhatsAppAudioContractCapture.WriteAsync(accepted,"audio-request",Ct);
        var initial=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(message=>message.Id==row.Message,Ct));Assert.NotNull(initial.AudioRequestId);Assert.Equal(first,initial.AudioActorUserId);
        using var replay=await client.SendAsync(HttpMethod.Post,route,userId:second.ToString());Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);
        var after=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(message=>message.Id==row.Message,Ct));Assert.Equal(initial.AudioRequestId,after.AudioRequestId);Assert.Equal(first,after.AudioActorUserId);
        Assert.Equal("Pending",(await replay.ReadJsonAsync()).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Audio_thread_producer_exposes_pending_and_completed_states_without_model_calls()
    {
        var row=await AddAudioAsync(Now,true);var actor=await OperatorAsync(WhatsAppPermissions.Read);using var client=factory.CreateClient();
        using var accepted=await client.SendAsync(HttpMethod.Post,$"/api/whatsapp/chats/{row.Contact}/messages/{row.Message}/audio-transcription",userId:actor.ToString());Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var route=$"/api/whatsapp/chats/{row.Contact}/messages";
        using var pending=await client.SendAsync(HttpMethod.Get,route,userId:actor.ToString());Assert.Equal(HttpStatusCode.OK,pending.StatusCode);
        var pendingMessage=Assert.Single((await pending.ReadJsonAsync()).GetProperty("messages").EnumerateArray());Assert.Equal("Audio",pendingMessage.GetProperty("kind").GetString());Assert.Equal("Pending",pendingMessage.GetProperty("audio").GetProperty("state").GetString());
        await WhatsAppAudioContractCapture.WriteAsync(pending,"audio-thread-pending",Ct);
        await factory.InTransactionAsync(async services=>
        {
            var message=await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(item=>item.Id==row.Message,Ct);
            Assert.True(message.CompleteAudio(message.AudioRequestId!.Value,"Visité el local y tomé una foto.",Now).IsSuccess);
        });
        using var completed=await client.SendAsync(HttpMethod.Get,route,userId:actor.ToString());Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
        var message=Assert.Single((await completed.ReadJsonAsync()).GetProperty("messages").EnumerateArray());Assert.Equal("Completed",message.GetProperty("audio").GetProperty("state").GetString());Assert.Equal("Visité el local y tomé una foto.",message.GetProperty("body").GetString());
        await WhatsAppAudioContractCapture.WriteAsync(completed,"audio-thread-completed",Ct);
    }
    [Fact]
    public async Task Audio_routes_require_read_and_conceal_cross_contact_messages()
    {
        var row=await AddAudioAsync(Now);var reader=await OperatorAsync(WhatsAppPermissions.Read);var sender=await OperatorAsync(WhatsAppPermissions.Send);using var client=factory.CreateClient();
        var route=$"/api/whatsapp/chats/{row.Contact}/messages/{row.Message}";
        using var anonymous=await client.SendAsync(HttpMethod.Post,route+"/audio-transcription");Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        using var forbidden=await client.SendAsync(HttpMethod.Post,route+"/audio-transcription",userId:sender.ToString());Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using var other=await client.SendAsync(HttpMethod.Post,$"/api/whatsapp/chats/{Guid.NewGuid()}/messages/{row.Message}/audio-transcription",userId:reader.ToString());Assert.Equal(HttpStatusCode.NotFound,other.StatusCode);
        using var missing=await client.SendAsync(HttpMethod.Get,route+"/audio",userId:reader.ToString());Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
    }
    [Fact]
    public async Task Private_audio_playback_supports_ranges_without_exposing_a_provider_url()
    {
        var row=await AddAudioAsync(Now,true);var actor=await OperatorAsync(WhatsAppPermissions.Read);var files=new ActionPrivateFiles();files.Content[row.Key]=AudioBytes();
        await using var host=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(files))));using var client=host.CreateClient();
        using var request=new HttpRequestMessage(HttpMethod.Get,$"/api/whatsapp/chats/{row.Contact}/messages/{row.Message}/audio");request.Headers.Range=new RangeHeaderValue(0,3);request.Headers.Add(TestAuthHandler.UserIdHeader,actor.ToString());
        using var full=await client.SendAsync(HttpMethod.Get,request.RequestUri!.ToString(),userId:actor.ToString());Assert.Equal(HttpStatusCode.OK,full.StatusCode);Assert.Equal("no-store",full.Headers.CacheControl!.ToString());Assert.Equal("audio/ogg",full.Content.Headers.ContentType!.MediaType);Assert.Equal(AudioBytes(),await full.Content.ReadAsByteArrayAsync(Ct));
        using var ranged=await client.SendAsync(request,Ct);
        Assert.Equal(HttpStatusCode.PartialContent,ranged.StatusCode);Assert.Equal("OggS"u8.ToArray(),await ranged.Content.ReadAsByteArrayAsync(Ct));
    }
    [Fact]
    public async Task Audio_references_protect_a_crash_object_until_message_retention_then_release_it()
    {
        var fresh=await AddAudioAsync(Now);var stored=await AddAudioAsync(Now,true);var expired=await AddAudioAsync(Now.AddDays(-91));
        var referenced=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().ListReferencedKeysAsync([fresh.Key,expired.Key],Now.AddDays(-90),Ct));
        Assert.Contains(fresh.Key,referenced);Assert.DoesNotContain(expired.Key,referenced);
        var pending=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(fresh.Contact,Now,Ct));Assert.NotNull(pending);Assert.Null(pending.StorageKey);
        var copied=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAudioReader>().FindPendingAsync(stored.Contact,Now,Ct));Assert.Null(copied);
    }
    [Fact]
    public async Task Retention_clears_audio_descriptor_blob_reference_and_transcript_while_preserving_the_message()
    {
        var row=await AddAudioAsync(Now.AddDays(-91),true);var actor=await OperatorAsync(WhatsAppPermissions.Read);
        await factory.InTransactionAsync(async services=>
        {
            var message=await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(item=>item.Id==row.Message,Ct);
            var request=message.RequestAudioNormalization(actor,"{}",0,Now.AddDays(-1),Now.AddDays(-91));
            Assert.True(request.IsSuccess);Assert.True(message.CompleteAudio(request.Value,"Transcripción privada que debe desaparecer.",Now.AddDays(-91)).IsSuccess);
        });
        var before=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(item=>item.Id==row.Message,Ct));
        Assert.Equal("Transcripción privada que debe desaparecer.",before.Body);
        await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppMessageRetentionRepository>().ClearExpiredBodiesAsync(Now.AddDays(-90),Ct));
        var message=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(item=>item.Id==row.Message,Ct));
        Assert.Equal(WhatsAppAudioState.Expired,message.AudioState);Assert.Null(message.Body);Assert.Null(message.AudioMediaId);Assert.Null(message.AudioStorageKey);Assert.Null(message.AudioSha256);Assert.Null(message.AudioContextJson);Assert.Equal(WhatsAppMessageKind.Audio,message.Kind);
    }
}
