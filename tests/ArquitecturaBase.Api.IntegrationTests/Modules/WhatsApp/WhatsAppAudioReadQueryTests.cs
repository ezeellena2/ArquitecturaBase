using System.Reflection;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAudioReadQueryTests
{
    [Theory]
    [InlineData(1,30)]
    [InlineData(2,90)]
    [InlineData(3,210)]
    [InlineData(4,450)]
    [InlineData(5,930)]
    public void Audio_failures_do_not_take_scan_slots_until_the_flow_retry_schedule_is_due(int failures,int seconds)
    {
        var clock=new FakeTimeProvider(new DateTimeOffset(2026,10,18,12,0,0,TimeSpan.Zero));var now=clock.GetUtcNow().UtcDateTime;
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.audio.retry",WhatsAppMessageKind.Audio,null,null,now);
        Assert.True(message.SetAudioDescriptor("123","audio/ogg",new string('a',64)));
        Assert.True(WhatsAppAudioReader.RetryDueAt(now).Compile()(message));
        for(var index=0;index<failures;index++)message.RegisterAudioFailure(now,"Files.Unavailable");
        clock.Advance(TimeSpan.FromSeconds(seconds-1));Assert.False(WhatsAppAudioReader.RetryDueAt(clock.GetUtcNow().UtcDateTime).Compile()(message));
        clock.Advance(TimeSpan.FromSeconds(1));Assert.True(WhatsAppAudioReader.RetryDueAt(clock.GetUtcNow().UtcDateTime).Compile()(message));
    }
    [Theory]
    [InlineData("Pending")]
    [InlineData("Rows")]
    public void Audio_projection_and_pending_selection_translate_without_opening_a_database(string methodName)
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAudioReader(db,new(90),new FakeTimeProvider(now));
        var method=typeof(WhatsAppAudioReader).GetMethod(methodName,BindingFlags.NonPublic|BindingFlags.Instance)!;
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppAudioSourceRow>>(method.Invoke(reader,methodName=="Pending"?[now]:null));
        query=query.Where(row=>row.ContactId==Guid.Empty).OrderBy(row=>row.OccurredAtUtc).ThenBy(row=>row.MessageId).Take(50);
        string? sql=null;var exception=Record.Exception(()=>sql=query.ToQueryString());Assert.Null(exception);Assert.NotNull(sql);
        Assert.Contains("INNER JOIN",sql,StringComparison.Ordinal);Assert.Contains("Audio",sql,StringComparison.Ordinal);Assert.Contains("LIMIT",sql,StringComparison.Ordinal);
        if(methodName=="Pending"){Assert.Contains("OccurredAtUtc",sql,StringComparison.Ordinal);Assert.Contains("AudioContentExpiresAtUtc",sql,StringComparison.Ordinal);Assert.Contains("Assistant",sql,StringComparison.Ordinal);Assert.Contains("\"AudioStorageKey\" IS NULL",sql,StringComparison.Ordinal);Assert.Contains("\"AudioFirstFailureAtUtc\" IS NULL",sql,StringComparison.Ordinal);}
    }
}
