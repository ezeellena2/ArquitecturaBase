using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I20: with thousands of purged turns, the purge, the measurement and the retirement of conversations read what is left to clean through
// their indexes, never the whole history: no plan reads the turns or the receipts sequentially. The selection itself is proven by the
// retention and removal tests; this one looks only at the plans PostgreSQL chooses for the statements that ran.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantRetentionPlanTests(ApiFactory factory)
{
    private const int Conversations = 600;
    private const int TurnsPerConversation = 9;
    private static readonly string[] LargeTables = ["AiAssistantTurns", "AiAssistantRequestReceipts"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_purge_and_the_measurement_never_read_the_history_sequentially()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        // The rows of this test carry a creation instant of their own, so it removes exactly them.
        var marker = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(Random.Shared.Next(1, 9_999_999));
        try
        {
            await SeedAsync(nowUtc, marker);
            var statements = await RecordAsync(nowUtc);

            var plans = new List<(string Sql, List<JsonElement> Nodes)>();
            foreach (var statement in statements) plans.Add((statement.Sql, await ExplainAsync(statement)));

            var sequential = plans.Where(plan => plan.Nodes.Any(node => node.GetProperty("Node Type").GetString() == "Seq Scan"
                && LargeTables.Contains(node.GetProperty("Relation Name").GetString()))).Select(plan => plan.Sql + " => " + string.Join(", ",
                plan.Nodes.Select(node => node.GetProperty("Node Type").GetString() + " " + (node.TryGetProperty("Relation Name", out var table) ? table.GetString() : "")
                    + (node.TryGetProperty("Index Name", out var index) ? " " + index.GetString() : "")))).ToArray();
            Assert.True(sequential.Length == 0, string.Join(Environment.NewLine, sequential));
            var indexes = plans.SelectMany(plan => plan.Nodes).Where(node => node.TryGetProperty("Index Name", out _))
                .Select(node => node.GetProperty("Index Name").GetString()).ToHashSet();
            Assert.Contains("IX_AiAssistantTurns_Content_ContentExpiresAtUtc_Id", indexes);
            Assert.Contains("IX_AiConversations_Removed_RemovalRequestedAtUtc_Id", indexes);
            Assert.Contains("IX_AiAssistantRequestReceipts_ConversationId_ExpiresAtUtc", indexes);
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("""DELETE FROM "AiAssistantRequestReceipts" WHERE "CreatedAtUtc" = @marker""",
                    [new NpgsqlParameter("marker", marker)], Ct);
                return await db.Database.ExecuteSqlRawAsync("""DELETE FROM "AiConversations" WHERE "CreatedAtUtc" = @marker""",
                    [new NpgsqlParameter("marker", marker)], Ct);
            });
        }
    }

    // Each conversation keeps a long history of purged turns and one turn with content, and each turn its receipt (live for that one).
    // A few rows give each branch something to find: content past its expiry, a removed conversation with content and the unnamed
    // recording and reading of I2, older than a day.
    private Task<int> SeedAsync(DateTime nowUtc, DateTime marker) => factory.ExecuteDbContextAsync(async db =>
    {
        NpgsqlParameter[] Values() => [new("marker", marker), new("past", nowUtc.AddDays(-40)), new("future", nowUtc.AddDays(20)),
            new("now", nowUtc), new("conversations", Conversations), new("turns", TurnsPerConversation)];
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AiConversations" ("Id", "OwnerUserId", "ChannelId", "State", "RemovalRequestedAtUtc", "LastActivityAtUtc", "CreatedAtUtc",
                "Revision", "RequiredPermissions")
            SELECT gen_random_uuid(), gen_random_uuid(), CASE WHEN n % 50 = 0 THEN 'home-audio' WHEN n % 50 = 1 THEN 'home-document' ELSE 'home' END,
                CASE WHEN n % 100 = 2 THEN 'Removed' ELSE 'Active' END, CASE WHEN n % 100 = 2 THEN @now END, @now, @marker, 1, '[]'::jsonb
            FROM generate_series(1, @conversations) AS n
            """, Values(), Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AiAssistantTurns" ("Id", "OwnerUserId", "ConversationId", "RequestId", "Ordinal", "ChannelId", "State", "Stage", "Culture",
                "TimeZoneId", "Provider", "Model", "ConnectionRevision", "SubmittedAtUtc", "ContentExpiresAtUtc", "CreatedAtUtc", "Fence",
                "PayloadRevision", "ProviderSteps", "ReportedUsageSteps", "Revision", "ToolBytes", "ToolCalls", "UsageUnknown", "ProtectedQuestion")
            SELECT gen_random_uuid(), c."OwnerUserId", c."Id", gen_random_uuid(), o, c."ChannelId", 'Completed', 'finished', 'es', 'UTC', 'OpenAI', 'model', 1,
                @past, CASE WHEN o = @turns AND c."ChannelId" = 'home' AND c."State" = 'Active' AND NOT c."Id"::text LIKE '0%' THEN @future ELSE @past END,
                @marker, 0, 1, 1, 1, 1, 0, 0, false, CASE WHEN o = @turns THEN 'protected-question' END
            FROM "AiConversations" c CROSS JOIN generate_series(1, @turns) AS o
            WHERE c."CreatedAtUtc" = @marker
            """, Values(), Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AiAssistantRequestReceipts" ("Id", "OwnerUserId", "RequestId", "ConversationId", "TurnId", "Fingerprint", "KeyId",
                "AcceptedAtUtc", "AcceptedDayUtc", "ExpiresAtUtc", "CreatedAtUtc", "IsDocument", "ChannelId", "IsAudio")
            SELECT gen_random_uuid(), t."OwnerUserId", t."RequestId", t."ConversationId", t."Id", 'fingerprint', 'test', t."SubmittedAtUtc",
                CAST(t."SubmittedAtUtc" AS date), CASE WHEN t."ContentExpiresAtUtc" > @now THEN @future ELSE @past END, @marker, false, t."ChannelId", false
            FROM "AiAssistantTurns" t WHERE t."CreatedAtUtc" = @marker
            """, Values(), Ct);
        await db.Database.ExecuteSqlRawAsync("""ANALYZE "AiConversations", "AiAssistantTurns", "AiAssistantRequestReceipts" """, Ct);
        return 0;
    });

    // What the purge, the measurement and the retirement run, recorded with their parameters.
    private async Task<IReadOnlyList<AssistantRecordedCommand>> RecordAsync(DateTime nowUtc)
    {
        var recorder = new AssistantReadCommandRecorder();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
        var repository = new AiAssistantRepository(db, factory.Clock);
        await repository.ListPurgeableAsync(nowUtc, new(nowUtc.AddHours(-24), "home-audio", "home-document", "ai/assistant/documents/",
            [".pdf", ".jpg", ".png"]), 128, Ct);
        await repository.ListDeletedOwnerConversationsAsync(128, Ct);
        await repository.ListRemovableConversationsAsync(nowUtc, 128, Ct);
        await repository.ListExpiredReceiptsAsync(nowUtc, 128, Ct);
        await new AiAssistantReader(db, factory.Clock).FindWorkloadAsync(nowUtc, 15, Ct);
        return recorder.Statements;
    }

    // EXPLAIN of a statement as it ran; every node of its plan, nested ones too. With a few thousand rows in a base the collection
    // shares, a sequential scan of the receipts sometimes came out cheaper, so the plan is asked with sequential scans off: PostgreSQL
    // still takes one only where no index answers the read. Without the new indexes it goes through older ones, so the test names them.
    private Task<List<JsonElement>> ExplainAsync(AssistantRecordedCommand statement) => factory.ExecuteDbContextAsync(async db =>
    {
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXPLAIN (FORMAT JSON) " + statement.Sql;
        foreach (var parameter in statement.Parameters) command.Parameters.Add(parameter.Clone());
        using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(Ct))!);
        var nodes = new List<JsonElement>();
        void Walk(JsonElement node)
        {
            nodes.Add(node.Clone());
            if (node.TryGetProperty("Plans", out var children)) foreach (var child in children.EnumerateArray()) Walk(child);
        }
        Walk(document.RootElement[0].GetProperty("Plan"));
        return nodes;
    });
}
