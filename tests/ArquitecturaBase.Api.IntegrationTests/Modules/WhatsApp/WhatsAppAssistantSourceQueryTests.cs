using System.Reflection;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAssistantSourceQueryTests
{
    [Theory]
    [InlineData("Audio")]
    [InlineData("Completed")]
    [InlineData("AudioContentExpiresAtUtc")]
    public void Current_normalized_audio_is_an_interpretable_source_with_SQL_guards(string expected)
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc)));
        var method=typeof(WhatsAppAssistantReader).GetMethod("Sources",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(method.Invoke(reader,null));
        var sql=query.Where(message=>message.ContactId==Guid.Empty).OrderByDescending(message=>message.InboundSequence).Select(message=>message.Id).Take(1).ToQueryString();
        Assert.Contains(expected,sql,StringComparison.Ordinal);
        Assert.Contains("ProcessedAtUtc",sql,StringComparison.Ordinal);Assert.Contains("OccurredAtUtc",sql,StringComparison.Ordinal);Assert.Contains("Body",sql,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(90,1)]
    [InlineData(1,90)]
    public void Audio_projection_keeps_message_identity_and_the_shortest_current_lifetime(int retentionDays,int pinnedDays)
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions{MessageRetentionDays=retentionDays}),new FakeTimeProvider(now));
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.source-audio",WhatsAppMessageKind.Audio,null,null,now.AddHours(-1));
        Assert.True(message.SetAudioDescriptor("12345","audio/ogg",new string('a',64)));Assert.True(message.StoreAudio("whatsapp/audio/"+message.Id.ToString("N")+".ogg",100).IsSuccess);
        var requested=message.RequestAudioNormalization(Guid.NewGuid(),"{}",0,now.AddDays(pinnedDays),now);Assert.True(requested.IsSuccess);
        Assert.True(message.CompleteAudio(requested.Value,"Visité la sucursal norte.",now).IsSuccess);
        var method=typeof(WhatsAppAssistantReader).GetMethod("SourceRows",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var rows=Assert.IsAssignableFrom<IQueryable<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSourceRow>>(method.Invoke(reader,new object[]{new[]{message}.AsQueryable()}));
        var source=Assert.Single(rows);Assert.Equal(message.Id,source.Id);Assert.Equal(message.Body,source.Body);Assert.Equal(WhatsAppMessageKind.Audio,message.Kind);
        var retentionExpiry=message.OccurredAtUtc.AddDays(retentionDays);var pinnedExpiry=message.AudioContentExpiresAtUtc!.Value;
        Assert.Equal(retentionExpiry<pinnedExpiry?retentionExpiry:pinnedExpiry,source.ContentExpiresAtUtc);
    }
}
