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
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantConversationScopeMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PreviousMigration = "20261021125010_AddWhatsAppFlowDeprecationBackoff";
    private const string ScopeMigration = "20261021180953_ScopeAssistantActiveTurnsByConversation";
    private const string ActiveIndex = "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId";

    [Fact]
    public async Task Nullable_conversation_scopes_preserve_legacy_work_and_refuse_a_lossy_downgrade()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb",
            factory.NewDatabaseConnectionString("assistant_conversation_scope")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var actor = Guid.CreateVersion7();
        const string channel = "fixture-manual";
        var legacyConversation = AiConversation.Create(actor, now, channel);
        var legacy = AiAssistantTurn.Create(actor, legacyConversation.Id, Guid.NewGuid(), 1, "sealed-legacy-question", "es", "UTC",
            AiProvider.OpenAI, "fixture-model", 1, now, now.AddDays(1), channel);
        Assert.True(legacy.SealExecutionProfile("historical-profile-without-scope").IsSuccess);
        try
        {
            await db.Database.MigrateAsync(Ct);
            db.AddRange(legacyConversation, legacy);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var retained = await db.Set<AiAssistantTurn>().AsNoTracking().SingleAsync(turn => turn.Id == legacy.Id, Ct);
            Assert.Null(retained.ChannelConversationId);
            Assert.Equal(legacy.ExecutionProfileJson, retained.ExecutionProfileJson);
            Assert.Equal(legacy.ProtectedQuestion, retained.ProtectedQuestion);
            Assert.Equal(legacy.RequestId, retained.RequestId);

            var conversationIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            foreach (var conversationId in conversationIds)
            {
                var context = new AssistantChannelContext(channel, actor, null, "es", "UTC", new([], []), [], [])
                    { InteractionMode = AssistantInteractionMode.Manual, ChannelConversationId = conversationId };
                var conversation = AiConversation.Create(actor, now, channel);
                var turn = AiAssistantTurn.Create(actor, conversation.Id, Guid.NewGuid(), 1, "sealed-scoped-question", "es", "UTC",
                    AiProvider.OpenAI, "fixture-model", 1, now, now.AddDays(1), channel, conversationId);
                Assert.True(turn.SealExecutionProfile(JsonSerializer.Serialize(
                    new AiAssistantExecutionProfileSnapshot("fixture-profile", 1, "capabilities-v1", 1, context), JsonSerializerOptions.Web)).IsSuccess);
                db.AddRange(conversation, turn);
            }
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            foreach (var duplicateScope in new Guid?[] { conversationIds[0], null })
            {
                var conversation = AiConversation.Create(actor, now, channel);
                var duplicate = AiAssistantTurn.Create(actor, conversation.Id, Guid.NewGuid(), 1, "not-accepted", "es", "UTC",
                    AiProvider.OpenAI, "fixture-model", 1, now, now.AddDays(1), channel, duplicateScope);
                db.AddRange(conversation, duplicate);
                var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
                var unique = Assert.IsType<PostgresException>(rejected.InnerException);
                Assert.Equal("23505", unique.SqlState);
                Assert.Equal(ActiveIndex, unique.ConstraintName);
                db.ChangeTracker.Clear();
            }
            var before = await db.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == actor).ToArrayAsync(Ct);
            Assert.Equal(3, before.Length);
            Assert.Equal(conversationIds.Order(), before.Where(turn => turn.ChannelConversationId.HasValue)
                .Select(turn => turn.ChannelConversationId!.Value).Order());
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct));
            Assert.Equal("P0001", downgrade.SqlState);
            Assert.Contains(ScopeMigration, await db.Database.GetAppliedMigrationsAsync(Ct));
            var after = await db.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == actor).ToArrayAsync(Ct);
            Assert.Equal(before.Length, after.Length);
            foreach (var original in before)
            {
                var current = Assert.Single(after, turn => turn.Id == original.Id);
                Assert.Equal(original.ChannelConversationId, current.ChannelConversationId);
                Assert.Equal(original.ProtectedQuestion, current.ProtectedQuestion);
                Assert.Equal(original.ExecutionProfileJson, current.ExecutionProfileJson);
                Assert.Equal(original.State, current.State);
                Assert.Equal(original.Fence, current.Fence);
            }
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
