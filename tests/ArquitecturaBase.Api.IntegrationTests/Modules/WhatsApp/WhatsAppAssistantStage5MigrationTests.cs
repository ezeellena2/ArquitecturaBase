using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantStage5MigrationTests(ApiFactory factory)
{
    private const string Before="20261017192801_EnableWhatsAppAssistant";
    private const string Stage5="20261017233619_AddFormAssistantSuggestionsAndAudio";
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Fact]
    public Task Up_preserves_confirmed_conversation_sessions_and_accepts_the_previous_binary_insert_contract()=>
        WithDatabaseAsync("assistant_stage5_up",async db=>
        {
            var latest=Latest(db);var migrator=db.GetService<IMigrator>();var now=factory.Clock.GetUtcNow().UtcDateTime;
            await migrator.MigrateAsync(Before,Ct);
            var prior=await WhatsAppAssistantMigrationSeed.InsertLegacyAsync(db,now,Ct);
            await migrator.MigrateAsync(latest,Ct);
            await AssertLegacyAsync(db,prior,now);
            await AssertSessionDefaultsAsync(db,prior);
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("AudioState" IS NULL AND "AudioRequestId" IS NULL AND "AudioContextJson" IS NULL
                    AND "AudioFailureCount"=0 AND "AssistantProcessedAtUtc" IS NULL AND "QuickReplyTitlesJson" IS NULL) AS "Value"
                FROM "WhatsAppMessages" WHERE "Id"={prior.MediaMessageId}
                """).SingleAsync(Ct));
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("AssistantModeRevision" IS NULL AND "QuickRepliesJson" IS NULL) AS "Value"
                FROM "WhatsAppOutgoingMessages" WHERE "Id"={prior.OutgoingId}
                """).SingleAsync(Ct));
            Assert.Equal(0,await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM "WhatsAppAssistantRequests"
                """).SingleAsync(Ct));
            var duringDeployment=await WhatsAppAssistantMigrationSeed.InsertLegacyAsync(db,now.AddMinutes(1),Ct);
            await AssertLegacyAsync(db,duringDeployment,now.AddMinutes(1));
            await AssertSessionDefaultsAsync(db,duringDeployment);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
        });

    [Fact]
    public Task Compatible_Down_clears_only_new_audio_transcripts_and_can_be_applied_again()=>
        WithDatabaseAsync("assistant_stage5_down_audio",async db=>
        {
            var latest=Latest(db);var migrator=db.GetService<IMigrator>();var now=factory.Clock.GetUtcNow().UtcDateTime;
            await migrator.MigrateAsync(Before,Ct);
            var legacy=await WhatsAppAssistantMigrationSeed.InsertLegacyAsync(db,now,Ct);
            var history=(await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray();
            await migrator.MigrateAsync(latest,Ct);
            var audio=Guid.NewGuid();var request=Guid.NewGuid();var wamid="wamid.migration.audio-"+audio.ToString("N");
            var hash=new string('a',64);var contextJson="{}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WhatsAppMessages" ("Id","ContactId","Direction","WaMessageId","Kind","Body","OccurredAtUtc","InboundSequence","Purpose","Origin",
                    "AudioState","AudioMediaId","AudioContentType","AudioSha256","AudioStorageKey","AudioSize","AudioRequestId","AudioActorUserId","AudioContextJson","AudioLinkRevision","AudioContentExpiresAtUtc","AudioFailureCount")
                VALUES ({audio},{legacy.ContactId},'Inbound',{wamid},'Audio','Synthetic normalized transcript',{now},3,'Business','Conversation',
                    'Completed','123','audio/ogg',{hash},'whatsapp/audio/migration.ogg',320,{request},{legacy.RecipientId},{contextJson}::jsonb,1,{now.AddDays(1)},0)
                """,Ct);
            await migrator.MigrateAsync(Before,Ct);
            await AssertLegacyAsync(db,legacy,now);
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("Kind"='Media' AND "Body" IS NULL AND "Direction"='Inbound' AND "Purpose"='Business'
                    AND "Origin"='Conversation' AND "ContactId"={legacy.ContactId} AND "OccurredAtUtc"={now} AND "InboundSequence"=3) AS "Value"
                FROM "WhatsAppMessages" WHERE "Id"={audio}
                """).SingleAsync(Ct));
            Assert.Equal(history,(await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray());
            Assert.Equal(0,await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
                WHERE table_schema='public' AND table_name='WhatsAppMessages' AND column_name='AudioState'
                """).SingleAsync(Ct));
            await migrator.MigrateAsync(latest,Ct);
            await AssertSessionDefaultsAsync(db,legacy);
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("Kind"='Media' AND "Body" IS NULL AND "AudioState" IS NULL AND "AudioRequestId" IS NULL AND "AudioFailureCount"=0) AS "Value"
                FROM "WhatsAppMessages" WHERE "Id"={audio}
                """).SingleAsync(Ct));
        });

    [Theory]
    [InlineData("SessionWithoutOperator")]
    [InlineData("MessageAssistant")]
    [InlineData("OutgoingAssistant")]
    [InlineData("QuickReplies")]
    public Task Unrepresentable_facts_reject_Down_before_changing_schema_history_or_confirmed_capture(string fact)=>
        WithDatabaseAsync("assistant_stage5_guard",async db=>
        {
            var migrator=db.GetService<IMigrator>();var now=factory.Clock.GetUtcNow().UtcDateTime;
            await migrator.MigrateAsync(Before,Ct);
            var legacy=await WhatsAppAssistantMigrationSeed.InsertLegacyAsync(db,now,Ct);
            await migrator.MigrateAsync(Stage5,Ct);
            await WhatsAppAssistantMigrationSeed.InsertGuardFactAsync(db,legacy,fact,now,Ct);
            var snapshot=await SnapshotAsync(db);var history=(await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray();
            Assert.Equal(Stage5,history[^1]);
            var failure=await Assert.ThrowsAsync<PostgresException>(()=>migrator.MigrateAsync(Before,Ct));
            Assert.Equal(PostgresErrorCodes.RaiseException,failure.SqlState);
            Assert.Equal("Stage five assistant facts cannot be represented by the previous schema.",failure.MessageText);
            Assert.Equal(snapshot,await SnapshotAsync(db));
            Assert.Equal(history,(await db.Database.GetAppliedMigrationsAsync(Ct)).ToArray());
            Assert.Contains(Stage5,await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("CaptureState"='Completed' AND "CompletedRecordId"={legacy.RecordId} AND "CompletedNumber"='MIG-000024') AS "Value"
                FROM "WhatsAppFlowSessions" WHERE "Id"={legacy.SessionId}
                """).SingleAsync(Ct));
            Assert.True(await db.Database.SqlQuery<bool>($"""
                SELECT ("FlowCaptureState"='Completed' AND "FlowRecordId"={legacy.RecordId} AND "FlowRecordNumber"='MIG-000024') AS "Value"
                FROM "WhatsAppMessages" WHERE "Id"={legacy.ReplyMessageId}
                """).SingleAsync(Ct));
        });

    private static string Latest(ApplicationDbContext db)
    {
        var migrations=db.Database.GetMigrations().ToArray();Assert.Contains(Before,migrations);
        var latest=migrations[^1];Assert.NotEqual(Before,latest);return latest;
    }
    private async Task WithDatabaseAsync(string prefix,Func<ApplicationDbContext,Task> action)
    {
        await using var host=factory.WithWebHostBuilder(builder=>builder.UseSetting("ConnectionStrings:appdb",factory.NewDatabaseConnectionString(prefix)));
        await using var scope=host.Services.CreateAsyncScope();
        await using var db=new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try{await action(db);}finally{await db.Database.EnsureDeletedAsync(Ct);}
    }
    private static async Task AssertSessionDefaultsAsync(ApplicationDbContext db,WhatsAppAssistantMigrationRows row)
    {
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT ("Origin"='Conversation' AND "OperatorUserId"={row.OperatorId}
                AND octet_length("ProtectedAssistantContent")=0 AND "AssistantContentExpiresAtUtc" IS NULL) AS "Value"
            FROM "WhatsAppFlowSessions" WHERE "Id"={row.SessionId}
            """).SingleAsync(Ct));
    }
    private static async Task AssertLegacyAsync(ApplicationDbContext db,WhatsAppAssistantMigrationRows row,DateTime nowUtc)
    {
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT ("OperatorUserId"={row.OperatorId} AND "ContactId"={row.ContactId} AND "UserId"={row.RecipientId}
                AND "DefinitionId"={row.DefinitionId} AND "VersionId"={row.VersionId} AND "VersionNumber"=1 AND "Culture"='es'
                AND "CaptureState"='Completed' AND "CompletedRecordId"={row.RecordId} AND "CompletedNumber"='MIG-000024'
                AND encode("ProtectedPayload",'hex')='070809' AND "CreatedAtUtc"={nowUtc} AND "ExpiresAtUtc"={nowUtc.AddHours(24)}) AS "Value"
            FROM "WhatsAppFlowSessions" WHERE "Id"={row.SessionId}
            """).SingleAsync(Ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT ("Body"='Legacy media annotation' AND "Kind"='Media' AND "Direction"='Inbound' AND "Purpose"='Business'
                AND "Origin"='Conversation' AND "ContactId"={row.ContactId} AND "OccurredAtUtc"={nowUtc} AND "InboundSequence"=1) AS "Value"
            FROM "WhatsAppMessages" WHERE "Id"={row.MediaMessageId}
            """).SingleAsync(Ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT ("FlowCaptureState"='Completed' AND "FlowRecordId"={row.RecordId} AND "FlowRecordNumber"='MIG-000024'
                AND "FlowSessionId"={row.SessionId} AND encode("ProtectedFlowResponse",'hex')='0c0d') AS "Value"
            FROM "WhatsAppMessages" WHERE "Id"={row.ReplyMessageId}
            """).SingleAsync(Ct));
        Assert.True(await db.Database.SqlQuery<bool>($"""
            SELECT ("OperatorUserId"={row.OperatorId} AND "ContactId"={row.ContactId} AND "RequestId"={row.RequestId}
                AND "Body"='Legacy manual form' AND "State"='Accepted' AND "Kind"=1 AND "Origin"='Conversation'
                AND "FlowSessionId"={row.SessionId} AND "RequestedAtUtc"={nowUtc} AND "WaMessageId" IS NOT NULL) AS "Value"
            FROM "WhatsAppOutgoingMessages" WHERE "Id"={row.OutgoingId}
            """).SingleAsync(Ct));
        Assert.Equal(2,await db.Database.SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "AspNetUsers" WHERE "Id" IN ({row.OperatorId},{row.RecipientId})""").SingleAsync(Ct));
    }
    private static Task<string> SnapshotAsync(ApplicationDbContext db)=>db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object(
            'contacts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppContacts" r),
            'operations',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppFlowOperations" r),
            'bindings',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppFlowBindings" r),
            'sessions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppFlowSessions" r),
            'messages',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppMessages" r),
            'outgoing',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppOutgoingMessages" r),
            'references',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "WhatsAppAssistantRequests" r),
            'users',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "Id") FROM "AspNetUsers" r),
            'history',(SELECT jsonb_agg(to_jsonb(r) ORDER BY "MigrationId") FROM "__EFMigrationsHistory" r),
            'columns',(SELECT jsonb_agg(jsonb_build_object('table',table_name,'name',column_name,'type',data_type,'nullable',is_nullable,'default',column_default)
                ORDER BY table_name,ordinal_position) FROM information_schema.columns
                WHERE table_schema='public' AND table_name IN ('WhatsAppFlowSessions','WhatsAppMessages','WhatsAppOutgoingMessages','WhatsAppAssistantRequests'))
        )::text AS "Value"
        """).SingleAsync(Ct);
}
