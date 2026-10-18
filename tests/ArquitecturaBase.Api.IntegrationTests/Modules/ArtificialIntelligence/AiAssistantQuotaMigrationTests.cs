using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantQuotaMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Receipt_backfill_uses_retained_channel_and_valid_profile_without_inventing_orphan_attribution()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_quota_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime; var owner = Guid.CreateVersion7();
        var channels = new[] { "home", "fixture-text", "fixture-audio", "fixture-no-profile", "fixture-invalid-profile" };
        var conversations = channels.Select(channel => AiConversation.Create(owner, now, channel)).ToArray();
        var turns = conversations.Select(conversation => AiAssistantTurn.Create(owner, conversation.Id, Guid.CreateVersion7(), 1,
            "fictional-protected", "es", "UTC", AiProvider.OpenAI, "fictional-model", 1, now, now.AddDays(1), conversation.ChannelId)).ToArray();
        for (var i = 1; i <= 2; i++)
        {
            var context = new AssistantChannelContext(channels[i], owner, owner, "es", "UTC", new([], []), [], []);
            var profile = new AiAssistantExecutionProfileSnapshot("fixture-profile", 1, i == 2 ? "audio-none-v1" : "capabilities-v1", 1, context)
                { Purpose = i == 2 ? AiAssistantPurpose.NormalizeAudio : AiAssistantPurpose.Interpret };
            Assert.True(turns[i].SealExecutionProfile(JsonSerializer.Serialize(profile, JsonSerializerOptions.Web)).IsSuccess);
        }
        var receipts = turns.Select(turn => AiAssistantRequestReceipt.Create(owner, turn.RequestId, "fictional", "test", turn.ConversationId,
            turn.Id, now, now.AddDays(1), turn.ChannelId, false)).ToList();
        var orphan = AiAssistantRequestReceipt.Create(owner, Guid.CreateVersion7(), "fictional", "test", Guid.CreateVersion7(), Guid.CreateVersion7(), now, now.AddDays(1), "unproven", false);
        receipts.Add(orphan);
        try
        {
            await db.Database.MigrateAsync(Ct);
            db.Set<AiConversation>().AddRange(conversations); db.Set<AiAssistantTurn>().AddRange(turns); db.Set<AiAssistantRequestReceipt>().AddRange(receipts);
            await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AiAssistantTurns" SET "ExecutionProfileJson" = 'not-json' WHERE "Id" = {turns[4].Id}""", Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync("20261018210104_ScopeAssistantActiveTurnsByChannel", Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var migrated = await db.Set<AiAssistantRequestReceipt>().AsNoTracking().Where(receipt => receipt.OwnerUserId == owner).ToArrayAsync(Ct);
            Assert.Equal(receipts.Count, migrated.Length);
            for (var i = 0; i < turns.Length; i++)
            {
                var row = Assert.Single(migrated, receipt => receipt.Id == receipts[i].Id);
                Assert.Equal(channels[i], row.ChannelId);
                Assert.Equal(i is 0 or 1 ? false : i == 2 ? true : (bool?)null, row.IsAudio);
                Assert.Equal(receipts[i].Fingerprint, row.Fingerprint);
                Assert.Equal(receipts[i].RequestId, row.RequestId);
            }
            var unknown = Assert.Single(migrated, receipt => receipt.Id == orphan.Id);
            Assert.Null(unknown.ChannelId); Assert.Null(unknown.IsAudio);
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
