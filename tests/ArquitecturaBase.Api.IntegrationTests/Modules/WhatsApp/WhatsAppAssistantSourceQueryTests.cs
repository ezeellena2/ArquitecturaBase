using System.Reflection;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
// The SQL of the sources is checked without opening a database; the pairing with the reply in between, with PostgreSQL.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantSourceQueryTests(ApiFactory factory)
{
    [Fact]
    public void A_terminal_source_marker_is_projected_without_hiding_the_source_from_reference_recovery()
    {
        var now=new DateTime(2026,10,18,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(now));
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.marked-source",WhatsAppMessageKind.Text,"A fictitious visit",null,now);
        message.MarkAssistantProcessed(now);
        var rows=Assert.IsAssignableFrom<IQueryable<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSourceRow>>(
            typeof(WhatsAppAssistantReader).GetMethod("SourceRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[new[]{message}.AsQueryable()]));
        Assert.Equal(now,Assert.Single(rows).AssistantProcessedAtUtc);
        var sources=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(typeof(WhatsAppAssistantReader).GetMethod("Sources",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,null));
        Assert.DoesNotContain("AssistantProcessedAtUtc",sources.Select(source=>source.Id).ToQueryString(),StringComparison.Ordinal);
    }

    [Fact]
    public void Automatic_new_work_scan_excludes_terminal_text_and_audio_sources_in_SQL()
    {
        var now=new DateTime(2026,10,18,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(now));
        var query=Assert.IsAssignableFrom<IQueryable<Guid>>(typeof(WhatsAppAssistantReader).GetMethod("PendingContactRows",BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(reader,[Array.Empty<Guid>(),50,now]));
        Assert.Matches("""["]Kind["] <> 'FlowReply' AND [a-zA-Z0-9_]+[.]["]AssistantProcessedAtUtc["] IS NULL""",query.ToQueryString());
    }

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
    [Theory]
    [InlineData("FlowReply")]
    [InlineData("FlowCaptureState")]
    [InlineData("FlowRecordId")]
    public void A_completed_capture_is_a_current_source_with_a_real_record_identity(string expected)
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc)));
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(typeof(WhatsAppAssistantReader).GetMethod("Sources",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,null));
        var sql=query.Select(message=>message.Id).ToQueryString();Assert.Contains(expected,sql,StringComparison.Ordinal);
    }
    [Fact]
    public void A_completed_capture_projection_preserves_wamid_record_and_original_lifetime_without_inventing_message_text()
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);var recordId=Guid.NewGuid();
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions{MessageRetentionDays=30}),new FakeTimeProvider(now));
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.captured-source",WhatsAppMessageKind.FlowReply,null,null,now.AddHours(-1),[1]);
        message.Classify(WhatsAppMessagePurpose.Business);Assert.True(message.SetFlowReplyCapture(WhatsAppFlowCaptureState.Completed,recordId,"VISIT-000024",null));message.MarkProcessed(now);
        var method=typeof(WhatsAppAssistantReader).GetMethod("SourceRows",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var rows=Assert.IsAssignableFrom<IQueryable<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSourceRow>>(method.Invoke(reader,[new[]{message}.AsQueryable()]));
        var source=Assert.Single(rows);Assert.Equal(message.Id,source.Id);Assert.Equal(message.WaMessageId,source.WaMessageId);
        Assert.Equal(recordId,source.CapturedRecordId);Assert.Equal(string.Empty,source.Body);
        Assert.Equal(message.OccurredAtUtc,source.OccurredAtUtc);Assert.Equal(message.OccurredAtUtc.AddDays(30),source.ContentExpiresAtUtc);
    }
    [Fact]
    public void Pending_reference_polling_reads_only_live_pending_references_without_scanning_new_sources()
    {
        var now=new DateTime(2026,10,18,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(now));
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppAssistantRequest>>(typeof(WhatsAppAssistantReader)
            .GetMethod("PendingReferences",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[now]));
        var sql=query.Select(row=>row.Id).ToQueryString();
        Assert.Contains("\"State\" = 'Pending'",sql,StringComparison.Ordinal);
        Assert.Contains("\"ContentExpiresAtUtc\" >",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("UNION",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("WhatsAppMessages",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("ContextJson",sql,StringComparison.Ordinal);
    }

    // IA, Stage 2: a stored spreadsheet is a source while its copy and its prepared sheet last, with or without a caption.
    [Theory]
    [InlineData("Spreadsheet")]
    [InlineData("Stored")]
    [InlineData("SpreadsheetPreparedJson")]
    [InlineData("SpreadsheetStorageKey")]
    public void A_stored_spreadsheet_is_a_source_with_SQL_guards(string expected)
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc)));
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppMessage>>(typeof(WhatsAppAssistantReader).GetMethod("Sources",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,null));
        Assert.Contains(expected,query.Select(message=>message.Id).ToQueryString(),StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Importala en Clientes","Importala en Clientes")]
    [InlineData(null,"")]
    public void A_stored_spreadsheet_projects_its_caption_its_copy_and_the_lifetime_of_its_message(string? caption,string body)
    {
        var now=new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions{MessageRetentionDays=30}),new FakeTimeProvider(now));
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.source-spreadsheet",WhatsAppMessageKind.Spreadsheet,caption,null,now.AddHours(-1));
        Assert.True(message.SetSpreadsheetDescriptor("1234","text/csv",new string('a',64),"clientes.csv"));
        Assert.True(message.StoreSpreadsheet($"whatsapp/documents/{message.Id:N}.csv",27,new string('b',64),"{\"rowCount\":3}").IsSuccess);
        var rows=Assert.IsAssignableFrom<IQueryable<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSourceRow>>(
            typeof(WhatsAppAssistantReader).GetMethod("SourceRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[new[]{message}.AsQueryable()]));
        var source=Assert.Single(rows);
        Assert.Equal((message.Id,body,WhatsAppMessageKind.Spreadsheet,message.OccurredAtUtc.AddDays(30)),(source.Id,source.Body,source.Kind,source.ContentExpiresAtUtc));
        var spreadsheet=Assert.IsType<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSpreadsheetRow>(source.Spreadsheet);
        Assert.Equal((message.Id,$"whatsapp/documents/{message.Id:N}.csv","clientes.csv",27L,new string('b',64),message.OccurredAtUtc.AddDays(30),"{\"rowCount\":3}"),
            (spreadsheet.MessageId,spreadsheet.StorageKey,spreadsheet.FileName,spreadsheet.Size,spreadsheet.Sha256,spreadsheet.ExpiresAtUtc,spreadsheet.PreparedJson));
    }

    // The rule changed: a source no longer stands alone, it pairs with the business message just before it, and an assistant reply
    // requested between the two (one that did not fail) keeps them apart.
    [Theory]
    [InlineData("\"InboundSequence\" <")]
    [InlineData("ORDER BY")]
    [InlineData("WhatsAppOutgoingMessages")]
    [InlineData("\"RequestedAtUtc\" >")]
    [InlineData("\"RequestedAtUtc\" <")]
    [InlineData("'Failed'")]
    [InlineData("\"Origin\"")]
    public void The_message_before_a_source_and_the_replies_in_between_are_read_in_SQL(string expected)
    {
        var now=new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAssistantReader(db,Options.Create(new WhatsAppMessageRetentionOptions()),new FakeTimeProvider(now));
        var source=new ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantSourceRow(Guid.NewGuid(),Guid.NewGuid(),"Armame un catálogo",now,now.AddDays(90)){InboundSequence=4};
        var query=Assert.IsAssignableFrom<IQueryable>(typeof(WhatsAppAssistantReader).GetMethod("PreviousRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[source]));
        Assert.Contains(expected,query.ToQueryString(),StringComparison.Ordinal);
    }

    // With PostgreSQL and the real reader: only an assistant reply requested after the message before and before the source keeps them
    // apart; one requested after the source, or one that failed, does not.
    [Theory]
    [InlineData("between",false)]
    [InlineData("failed-between",true)]
    [InlineData("after-the-source",true)]
    public async Task Only_a_reply_that_did_not_fail_requested_between_the_two_messages_keeps_them_apart(string reply,bool paired)
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        var person=await host.ContactAsync(Ct,automatic:true);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        await host.InTransactionAsync(async services=>
        {
            var db=services.GetRequiredService<ApplicationDbContext>();
            var contact=await db.Set<WhatsAppContact>().SingleAsync(row=>row.Id==person.ContactId,Ct);
            Add(db,contact,WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Text,"Armame un catálogo de clientes",null,now.AddMinutes(-3)),now);
            var outgoing=WhatsAppOutgoingMessage.CreateAssistantText(person.RecipientId,contact.Id,Guid.NewGuid(),"Respuesta ficticia",
                reply=="after-the-source"?now.AddSeconds(-30):now.AddMinutes(-2),contact.LinkRevision,contact.WaId!,contact.ModeRevision);
            if(reply=="failed-between")outgoing.Fail();
            db.Add(outgoing);
            var spreadsheet=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Spreadsheet,null,null,now.AddMinutes(-1));
            Assert.True(spreadsheet.SetSpreadsheetDescriptor("1234","text/csv",new string('a',64),"clientes.csv"));
            Assert.True(spreadsheet.StoreSpreadsheet($"whatsapp/documents/{spreadsheet.Id:N}.csv",27,new string('b',64),"{\"rowCount\":2}").IsSuccess);
            Add(db,contact,spreadsheet,now);
            return true;
        },Ct);

        var source=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestSourceAsync(person.ContactId,Ct));

        Assert.Equal(paired?("Armame un catálogo de clientes",WhatsAppAssistantSourceState.Ready):("",WhatsAppAssistantSourceState.Alone),(source!.Body,source.State));
    }

    // H11: a spreadsheet whose download keeps failing holds back the text after it only for the usual window (five failures and
    // fifteen minutes since the first); then the text goes on alone while the spreadsheet keeps retrying.
    [Theory]
    [InlineData(14,WhatsAppAssistantSourceState.Waiting)]
    [InlineData(15,WhatsAppAssistantSourceState.Ready)]
    public async Task A_text_waits_for_a_failing_spreadsheet_only_for_the_usual_window(int minutes,WhatsAppAssistantSourceState expected)
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        var person=await host.ContactAsync(Ct,automatic:true);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        await host.InTransactionAsync(async services=>
        {
            var db=services.GetRequiredService<ApplicationDbContext>();
            var contact=await db.Set<WhatsAppContact>().SingleAsync(row=>row.Id==person.ContactId,Ct);
            var spreadsheet=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Spreadsheet,null,null,now.AddMinutes(-minutes-1));
            Assert.True(spreadsheet.SetSpreadsheetDescriptor("1234","text/csv",new string('a',64),"clientes.csv"));
            for(var failure=0;failure<5;failure++)spreadsheet.RegisterSpreadsheetFailure(now.AddMinutes(-minutes),"Files.Unavailable");
            Add(db,contact,spreadsheet,now);
            Add(db,contact,WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Text,"Cargá esta planilla",null,now.AddMinutes(-minutes)),now);
            return true;
        },Ct);

        var source=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestSourceAsync(person.ContactId,Ct));

        Assert.Equal(("Cargá esta planilla",expected,(WhatsAppAssistantSpreadsheetRow?)null),(source!.Body,source.State,source.Spreadsheet));
    }

    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    private static void Add(ApplicationDbContext db,WhatsAppContact contact,WhatsAppMessage message,DateTime nowUtc)
    {
        message.Classify(WhatsAppMessagePurpose.Business);
        message.SetInboundSequence(contact.RegisterInboundMessage());
        message.MarkProcessed(nowUtc);
        db.Add(message);
    }}
