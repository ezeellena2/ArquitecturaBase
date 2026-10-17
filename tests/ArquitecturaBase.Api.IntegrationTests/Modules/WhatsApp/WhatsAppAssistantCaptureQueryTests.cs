using System.Reflection;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantCaptureQueryTests
{
    [Theory]
    [InlineData("same-record")]
    [InlineData("other-record")]
    [InlineData("text")]
    [InlineData("pending-audio")]
    public void The_first_completed_receipt_is_canonical_only_while_the_latest_business_receipt_confirms_the_same_record(string latestKind)
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);var contact=Guid.NewGuid();var record=Guid.NewGuid();
        var original=Reply(contact,record,1,now.AddHours(-1));
        var latest=latestKind is "same-record" or "other-record" ? Reply(contact,latestKind=="same-record"?record:Guid.NewGuid(),2,now)
            :WhatsAppMessage.Inbound(contact,"wamid.latest-"+Guid.NewGuid().ToString("N"),latestKind=="text"?WhatsAppMessageKind.Text:WhatsAppMessageKind.Audio,
                latestKind=="text"?"A later request":null,null,now);
        if(latestKind is "text" or "pending-audio")latest.SetInboundSequence(2);
        var method=typeof(WhatsAppAssistantReader).GetMethod("CurrentSourceMessages",BindingFlags.Static|BindingFlags.NonPublic)!;
        var rows=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(method.Invoke(null,[new[]{original,latest}.AsQueryable()]));
        var source=Assert.Single(rows);Assert.Equal(latestKind=="same-record"?original.Id:latest.Id,source.Id);
        Assert.Equal(latestKind=="same-record"?original.OccurredAtUtc:latest.OccurredAtUtc,source.OccurredAtUtc);
    }

    [Theory]
    [InlineData("CurrentReferenceRows","FlowRecordId")]
    [InlineData("PendingContactRows","SourceRecordId")]
    public void Pointer_and_pending_scan_translate_the_real_completed_record_identity_in_SQL(string methodName,string expected)
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(now));
        var method=typeof(WhatsAppAssistantReader).GetMethod(methodName,BindingFlags.Instance|BindingFlags.NonPublic)!;
        object?[] args=methodName=="CurrentReferenceRows"?[Guid.NewGuid()]:[Array.Empty<Guid>(),50,now];
        var query=Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader,args));
        Assert.Contains(expected,query.ToQueryString(),StringComparison.Ordinal);
    }
    [Fact]
    public void Canonical_capture_selection_translates_without_opening_a_database()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method=typeof(WhatsAppAssistantReader).GetMethod("CurrentSourceMessages",BindingFlags.Static|BindingFlags.NonPublic)!;
        var rows=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(method.Invoke(null,[db.Set<WhatsAppMessage>().AsNoTracking().Where(message=>message.ContactId==Guid.Empty)]));
        Assert.Contains("FlowRecordId",rows.Select(message=>message.Id).ToQueryString(),StringComparison.Ordinal);
    }
    private static WhatsAppMessage Reply(Guid contact,Guid record,long sequence,DateTime nowUtc)
    {
        var message=WhatsAppMessage.Inbound(contact,"wamid.capture-"+Guid.NewGuid().ToString("N"),WhatsAppMessageKind.FlowReply,null,null,nowUtc,[1]);
        message.SetInboundSequence(sequence);message.Classify(WhatsAppMessagePurpose.Business);
        Assert.True(message.SetFlowReplyCapture(WhatsAppFlowCaptureState.Completed,record,"VISIT-000024",null));message.MarkProcessed(nowUtc);return message;
    }
}
