using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed record WhatsAppAssistantMigrationRows(Guid ContactId,Guid OperatorId,Guid RecipientId,Guid DefinitionId,
    Guid VersionId,Guid SessionId,Guid MediaMessageId,Guid ReplyMessageId,Guid OutgoingId,Guid RecordId,Guid RequestId);

internal static class WhatsAppAssistantMigrationSeed
{
    // Deliberately omits every Stage5 column, even when called after Up: it is the old binary's INSERT contract.
    public static async Task<WhatsAppAssistantMigrationRows> InsertLegacyAsync(ApplicationDbContext db,DateTime nowUtc,CancellationToken ct)
    {
        var rows=new WhatsAppAssistantMigrationRows(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var operation=Guid.NewGuid();var neutralOperation=Guid.NewGuid();var binding=Guid.NewGuid();
        var waId=MetaWebhook.UniqueWaId();var userIdentifier=MetaWebhook.UniqueBsuid();
        var receivedWamid="wamid.migration.reply-"+rows.ReplyMessageId.ToString("N");
        var mediaWamid="wamid.migration.media-"+rows.MediaMessageId.ToString("N");
        var sentWamid="wamid.migration.sent-"+rows.OutgoingId.ToString("N");
        var nameJson="{\"es\":\"Legacy form\",\"en\":\"Legacy form\"}";
        var tokenHash=rows.SessionId.ToString("N")+rows.SessionId.ToString("N");
        var fingerprint=new string('a',64);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AspNetUsers" ("Id","DisplayName","Culture","TimeZoneId","IsActive","IsDeleted","EmailConfirmed",
                "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","CreatedAtUtc")
            VALUES ({rows.OperatorId},'Legacy operator','es','America/Argentina/Buenos_Aires',true,false,false,false,false,false,0,{nowUtc}),
                   ({rows.RecipientId},'Legacy recipient','es','America/Argentina/Buenos_Aires',true,false,false,false,false,false,0,{nowUtc});
            INSERT INTO "WhatsAppContacts" ("Id","WaId","UserIdentifier","UserId","ProfileName","LastInboundAtUtc","LastInboundSequence","Mode","ModeRevision","LinkRevision")
            VALUES ({rows.ContactId},{waId},{userIdentifier},{rows.RecipientId},'Legacy contact',{nowUtc},2,'Manual',7,1);
            INSERT INTO "WhatsAppFlowOperations" ("Id","NeutralOperationId","DefinitionId","VersionId","ActorUserId","ProtectedManifest","Fence","Completed","CreatedAtUtc")
            VALUES ({operation},{neutralOperation},{rows.DefinitionId},{rows.VersionId},{rows.OperatorId},decode('010203','hex'),0,true,{nowUtc});
            INSERT INTO "WhatsAppFlowBindings" ("Id","OperationId","VersionId","Culture","TechnicalName","ProtectedProjection","State","Step","Fence","InFlight","DiagnosticsJson","CreatedAtUtc")
            VALUES ({binding},{operation},{rows.VersionId},'es','legacy-migration',decode('040506','hex'),'Published','Verify',0,false,'[]'::jsonb,{nowUtc});
            INSERT INTO "WhatsAppFlowSessions" ("Id","ContactId","UserId","OperatorUserId","DefinitionId","VersionId","VersionNumber","BindingId",
                "Culture","NameJson","Prefix","TokenHash","RequestFingerprint","ProtectedPayload","ExpiresAtUtc","CaptureState",
                "CompletedRecordId","CompletedNumber","ResponseWaMessageId","LinkRevision","CreatedAtUtc")
            VALUES ({rows.SessionId},{rows.ContactId},{rows.RecipientId},{rows.OperatorId},{rows.DefinitionId},{rows.VersionId},1,{binding},
                'es',{nameJson}::jsonb,'MIG',{tokenHash},{fingerprint},decode('070809','hex'),{nowUtc.AddHours(24)},'Completed',
                {rows.RecordId},'MIG-000024',{receivedWamid},1,{nowUtc});
            INSERT INTO "WhatsAppMessages" ("Id","ContactId","Direction","WaMessageId","Kind","Body","OccurredAtUtc","InboundSequence","Purpose","Origin")
            VALUES ({rows.MediaMessageId},{rows.ContactId},'Inbound',{mediaWamid},'Media','Legacy media annotation',{nowUtc},1,'Business','Conversation');
            INSERT INTO "WhatsAppMessages" ("Id","ContactId","Direction","WaMessageId","Kind","OccurredAtUtc","InboundSequence","Purpose","Origin",
                "ProcessedAtUtc","FlowSessionId","FlowCaptureState","FlowRecordId","FlowRecordNumber","ProtectedFlowResponse")
            VALUES ({rows.ReplyMessageId},{rows.ContactId},'Inbound',{receivedWamid},'FlowReply',{nowUtc},2,'Business','Conversation',
                {nowUtc},{rows.SessionId},'Completed',{rows.RecordId},'MIG-000024',decode('0c0d','hex'));
            INSERT INTO "WhatsAppOutgoingMessages" ("Id","OperatorUserId","ContactId","RequestId","Body","RequestedAtUtc","State","Kind","Origin","FlowSessionId","WaMessageId","LastFormActionControlRevision")
            VALUES ({rows.OutgoingId},{rows.OperatorId},{rows.ContactId},{rows.RequestId},'Legacy manual form',{nowUtc},'Accepted',1,'Conversation',{rows.SessionId},{sentWamid},0);
            """,ct);
        return rows;
    }

    public static Task InsertGuardFactAsync(ApplicationDbContext db,WhatsAppAssistantMigrationRows rows,string fact,DateTime nowUtc,CancellationToken ct)
    {
        var id=Guid.NewGuid();var request=Guid.NewGuid();var wamid="wamid.migration.new-"+id.ToString("N");
        if(fact=="SessionWithoutOperator") return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "WhatsAppFlowSessions" SET "Origin"='Assistant',"OperatorUserId"=NULL,
                "ProtectedAssistantContent"=decode('a1a2','hex'),"AssistantContentExpiresAtUtc"={nowUtc.AddDays(1)} WHERE "Id"={rows.SessionId}
            """,ct);
        if(fact=="MessageAssistant") return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppMessages" ("Id","ContactId","Direction","WaMessageId","Kind","Body","OccurredAtUtc","InboundSequence","Purpose","Origin")
            VALUES ({id},{rows.ContactId},'Outbound',{wamid},'Text','Static assistant message',{nowUtc},0,'Business','Assistant')
            """,ct);
        if(fact=="OutgoingAssistant") return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppOutgoingMessages" ("Id","ContactId","RequestId","Body","RequestedAtUtc","State","Kind","Origin",
                "RecipientUserId","RecipientLinkRevision","RecipientWaId","AssistantModeRevision","LastFormActionControlRevision")
            VALUES ({id},{rows.ContactId},{request},'Static assistant outgoing',{nowUtc},'Accepted',0,'Assistant',
                {rows.RecipientId},1,'5493415550199',7,0)
            """,ct);
        ArgumentOutOfRangeException.ThrowIfNotEqual(fact,"QuickReplies");
        var replies=JsonSerializer.Serialize(new[]{
            new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request,rows.DefinitionId,rows.VersionId),"Visita",1),
            new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request,Guid.NewGuid(),Guid.NewGuid()),"Mantenimiento",1)});
        // Kind is the existing integer enum; isolate the new kind from the independent Assistant-origin guard.
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppOutgoingMessages" ("Id","OperatorUserId","ContactId","RequestId","Body","RequestedAtUtc","State","Kind","Origin","QuickRepliesJson","LastFormActionControlRevision")
            VALUES ({id},{rows.OperatorId},{rows.ContactId},{request},'Static choice',{nowUtc},'Accepted',3,'Conversation',{replies}::jsonb,0)
            """,ct);
    }
}
